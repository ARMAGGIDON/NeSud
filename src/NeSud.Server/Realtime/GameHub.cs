using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;

namespace NeSud.Server;

// Authentication is sent inside the encrypted WebSocket, not in its URL.
// REST and the hub call the same authoritative session operations.
public sealed class GameHub(ServerStore store, ILogger<GameHub> logger) : Hub
{
    private const string TokenKey = "token";
    private const string NameKey = "account";
    internal const string RankingsGroup = "rankings";

    internal static string Group(string name) => "account:" + name;

    public async Task<bool> Subscribe(string token)
    {
        if (Context.Items.ContainsKey(TokenKey) || token is null || store.WebUser(token) is not { } name)
        {
            return false;
        }
        Context.Items[TokenKey] = token;
        Context.Items[NameKey] = name;
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(name));
        return true;
    }

    private string? Account()
    {
        if (Context.Items.TryGetValue(TokenKey, out var token) && token is string secret &&
            Context.Items.TryGetValue(NameKey, out var expected) && store.WebUser(secret) is { } name &&
            Equals(name, expected)) return name;
        return null;
    }

    public async Task<bool> WatchRankings(bool watch)
    {
        if (Account() is null) return false;
        if (watch) await Groups.AddToGroupAsync(Context.ConnectionId, RankingsGroup);
        else await Groups.RemoveFromGroupAsync(Context.ConnectionId, RankingsGroup);
        return true;
    }

    public async Task<Dictionary<string, object?>> Snapshot(string? day, bool pause)
    {
        if (Account() is not { } name) return Wire.Error(401);
        var result = Endpoints.ReadGame(store, name, pause, day);
        if (pause) await Publish(name, day, result);
        return Wire.Response(result);
    }

    public async Task<Dictionary<string, object?>> New(int difficulty, string? id, long revision, bool autoFinish, string? day)
    {
        if (Account() is not { } name) return Wire.Error(401);
        if (id is not null && !Guid.TryParse(id, out _)) return Wire.Error(400);
        var request = new NewGameRequest((NeSud.Core.Difficulty)difficulty,
            id is null ? null : Guid.Parse(id), revision, autoFinish, day);
        var result = await Endpoints.NewGame(store, name, request, Context.ConnectionAborted);
        await Publish(name, day, result);
        if (!ServerStore.IsGuest(name) && result is IStatusCodeHttpResult { StatusCode: 200 })
            await NotifyRankings(Clients.Group(RankingsGroup));
        return Wire.Response(result);
    }

    public async Task<Dictionary<string, object?>> Move(string id, long revision, string action,
        int cell, int digit, bool note, string? reward, string? day)
    {
        if (Account() is not { } name) return Wire.Error(401);
        if (!Guid.TryParse(id, out var gameId) || action is null) return Wire.Error(400);
        Dictionary<string, object?>? before = null;
        var request = new MoveRequest(gameId, revision, action, cell, digit, note, reward, day);
        var result = Endpoints.MoveGame(store, name, request,
            action == "heartbeat" ? null : value => before = Wire.Map(value));
        var response = Wire.Response(result);
        if (response["data"] is Dictionary<string, object?> data)
        {
            if (action != "heartbeat" || data.TryGetValue("revision", out var next) && Convert.ToInt64(next) != revision)
                await Publish(name, day, result);
            if (!ServerStore.IsGuest(name) &&
                (data.GetValueOrDefault("completed") is true || data.GetValueOrDefault("finalDefeat") is true))
            {
                await NotifyRankings(Clients.Group(RankingsGroup));
                await NotifyAccount(Clients.OthersInGroup(Group(name)));
            }
            else if (action == "wallet") await NotifyAccount(Clients.OthersInGroup(Group(name)));
            if (before is not null && data.ContainsKey("values"))
                response["data"] = Wire.Delta(before, data, revision);
        }
        return response;
    }

    private async Task Publish(string name, string? day, IResult result)
    {
        try { await Notify(Clients.OthersInGroup(Group(name)), day, result); }
        catch (Exception ex) { logger.LogDebug(ex, "Realtime notification failed"); }
    }

    internal static async Task Notify(IClientProxy clients, string? day, IResult result)
    {
        if (result is not IStatusCodeHttpResult { StatusCode: 200 } ||
            result is not IValueHttpResult { Value: not null } value) return;
        // Invalidation only; receivers authenticate again before requesting a snapshot.
        try { await clients.SendAsync("GameChanged", day); } catch { }
    }

    internal static async Task NotifyRankings(IClientProxy clients)
    {
        try { await clients.SendAsync("RankingsChanged"); } catch { }
    }

    internal static async Task NotifyAccount(IClientProxy clients)
    {
        try { await clients.SendAsync("AccountChanged"); } catch { }
    }
}

internal static class Wire
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static Dictionary<string, object?> Map(object value) =>
        (Dictionary<string, object?>)ConvertElement(JsonSerializer.SerializeToElement(value, Json))!;

    private static object? ConvertElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ConvertElement(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().Select(ConvertElement).ToArray(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out long integer) ? (object)integer : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };

    internal static Dictionary<string, object?> Error(int status) => new()
    {
        ["status"] = status, ["data"] = null
    };

    internal static Dictionary<string, object?> Response(IResult result)
    {
        int status = (result as IStatusCodeHttpResult)?.StatusCode ?? 200;
        object? value = (result as IValueHttpResult)?.Value;
        return new() { ["status"] = status, ["data"] = value is null ? null : Map(value) };
    }

    private static bool Same(object? left, object? right)
    {
        if (left is object?[] a && right is object?[] b)
            return a.Length == b.Length && a.Zip(b).All(pair => Same(pair.First, pair.Second));
        if (left is Dictionary<string, object?> x && right is Dictionary<string, object?> y)
            return x.Count == y.Count && x.All(pair => y.TryGetValue(pair.Key, out var v) && Same(pair.Value, v));
        return Equals(left, right);
    }

    internal static Dictionary<string, object?> Delta(Dictionary<string, object?> before,
        Dictionary<string, object?> after, long revision)
    {
        var changes = new Dictionary<string, object?>();
        foreach (var (key, value) in after)
        {
            // Clock fields can change independently of the game revision.
            if (key is "seconds" or "clockFraction" or "currentScore" or "reviveSeconds" or "reviveRemainingMs" or "reward" ||
                !before.TryGetValue(key, out var old) || !Same(old, value))
                changes[key] = value;
        }
        return new()
        {
            ["delta"] = true, ["id"] = after["id"], ["baseRevision"] = revision,
            ["revision"] = after["revision"], ["changes"] = changes
        };
    }
}
