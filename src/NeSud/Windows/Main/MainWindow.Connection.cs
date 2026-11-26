using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NeSud.Core;
using NeSud.Storage;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;

namespace NeSud;

public sealed partial class MainWindow : Window
{
    private async Task<T> Request<T > (string path, object? body = null, string? endpoint = null, string? token = null)
    {
        string url = endpoint ?? serverUrl;
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post,
            url.TrimEnd('/') + "/api/" + path);
        var auth = token ?? account?.Token;
        if (!string.IsNullOrEmpty(auth)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            string? error = null;
            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (json.RootElement.TryGetProperty("error", out var value) || json.RootElement.TryGetProperty("detail", out value)) error = value.GetString();
                }
                catch (System.Text.Json.JsonException) { }
            throw new HttpRequestException(error ?? (response.StatusCode == HttpStatusCode.TooManyRequests ? "Забагато спроб. Спробуйте пізніше." : "Сервіс тимчасово недоступний."), null, response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<T > () ?? throw new InvalidDataException("Порожня відповідь сервера.");
    }

    private async Task Sync()
    {
        if (syncing || account is null || closing) return;
        syncing = true;
        nextSync = DateTimeOffset.UtcNow.AddSeconds(30);
        var session = account;
        var scope = Scope;
        var endpoint = serverUrl;
        try
        {
            var pending = store.History(scope, true);
            DesktopDraft? draft = G is { Completed: false } && (state.GameScope ?? "") == scope
                ? new(state.GameId, state.Started, DateTimeOffset.UtcNow, Database.Decode<Game > (Database.Encode(G))) : null;
            foreach (var batch in pending.Chunk(10))
            {
                var records = await Request<List<GameRecord>>("sync", batch, endpoint, session.Token);
                if (closing) return;
                store.Merge(scope, records);
            }
            if (draft is not null)
            {
                string fingerprint = draft.Id + ":" + Database.Encode(draft.Game);
                if (draftFingerprints.GetValueOrDefault(scope) != fingerprint)
                {
                    await Request<SyncAck > ("desktop/draft", draft, endpoint, session.Token);
                    draftFingerprints[scope] = fingerprint;
                }
            }
            long cursor = historyCursors.GetValueOrDefault(scope);
            while (true)
            {
                var page = await Request<HistoryPage > ($"history/changes?after={cursor}", endpoint: endpoint, token: session.Token);
                if (closing) return;
                if (page.Records.Count > 0) store.Merge(scope, page.Records);
                cursor = page.Cursor;
                historyCursors[scope] = cursor;
                if (page.Records.Count < 100) break;
            }
            if (ReferenceEquals(account, session))
            {
                reachable = true;
                await ResumeSpend();
                var updated = await Request<DailyStatus > ("daily", token: session.Token);
                if (ReferenceEquals(account, session) && !closing)
                {
                    daily = updated;
                    Render();
                }
            }
        }
        catch (Exception ex)
        {
            if (!closing && ReferenceEquals(account, session))
            {
                if (ex is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized })
                { account = null;
                state.ProtectedSession = null;
                Save(); }
                else
                {
                    reachable = false;
                    nextSync = DateTimeOffset.UtcNow.AddSeconds(5);
                }
                UpdateConnection();
            }
        }
        finally
        {
            syncing = false;
        }
    }

    private static string ServiceUrl()
    {
        var configured = typeof(MainWindow).Assembly.GetCustomAttributes<AssemblyMetadataAttribute > ()
            .FirstOrDefault(a => a.Key == "NeSudServerUrl")?.Value ?? "http://localhost:5080";
        var url = Environment.GetEnvironmentVariable("NESUD_SERVER_URL") ?? configured;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback) &&
            uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0)
            return url.TrimEnd('/');
        return "http://localhost:5080";
    }

    private AccountSession? RestoreSession()
    {
        try
        {
            if (state.ProtectedSession is null || !Scope.StartsWith(serverUrl + "|", StringComparison.Ordinal)) return null;
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(state.ProtectedSession), null, DataProtectionScope.CurrentUser);
            var restored = System.Text.Json.JsonSerializer.Deserialize<AccountSession > (bytes);
            return restored is not null && Scope == serverUrl + "|" + restored.User ? restored : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void RememberSession(AccountSession session)
    {
        try
        {
            state.ProtectedSession = Convert.ToBase64String(ProtectedData.Protect(
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(session), null, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            state.ProtectedSession = null;
        }
    }

    private void UpdateConnection()
    {
        if (stats is null) return;
        RefreshAccountAvatar();
        stats.Text = account is not null ? (reachable ? "● Онлайн • " : "○ Офлайн • ") + (account.Username ?? account.User) :
            Scope.Length > 0 ? "Потрібен повторний вхід • локальні дані збережено" :
            reachable ? "● Онлайн • гість" : "○ Офлайн • гість";
    }

    private async Task Probe()
    {
        if (closing || probing) return;
        probing = true;
        bool wasReachable = reachable;
        try
        {
            await Request<SyncAck > ("health", token: "");
            reachable = true;
        }
        catch
        {
            reachable = false;
        }
        finally
        {
            probing = false;
            nextProbe = DateTimeOffset.UtcNow.AddSeconds(reachable ? 15 : 5);
            if (!closing)
            {
                UpdateConnection();
                if (reachable && !wasReachable && account is not null)
                {
                    nextSync = DateTimeOffset.MinValue;
                    _ = Sync();
                }
            }
        }
    }
}
