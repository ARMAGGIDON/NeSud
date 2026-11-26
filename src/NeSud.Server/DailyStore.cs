using Microsoft.Data.Sqlite;
using NeSud.Core;
using NeSud.Storage;
using System.Security.Cryptography;

namespace NeSud.Server;

public sealed partial class ServerStore
{
    private static readonly TimeZoneInfo DailyZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");
    private static string Day(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, DailyZone).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    private static SqliteCommand Tx(SqliteConnection c, SqliteTransaction tx, string sql, params (string Name, object? Value)[] args)
    {
        var cmd = Database.Command(c, sql, args); cmd.Transaction = tx; return cmd;
    }
    private static RewardItem[] Options(int amount)
    {
        string[] kinds = ["hint", "candidates"];
        // Only the resource type is random. All three cards have the guaranteed milestone amount.
        return Enumerable.Range(0, 3).Select(_ => new RewardItem(kinds[RandomNumberGenerator.GetInt32(kinds.Length)], amount)).ToArray();
    }
    private static void AddPack(SqliteConnection c, SqliteTransaction tx, string name, string day, string milestone, int slot, int amount)
    {
        using var cmd = Tx(c, tx, "INSERT OR IGNORE INTO daily_packs(name,day,milestone,slot,options) VALUES($n,$d,$m,$s,$o)",
            ("$n", name), ("$d", day), ("$m", milestone), ("$s", slot), ("$o", Database.Encode(Options(amount))));
        cmd.ExecuteNonQuery();
    }
    private static int EnsureWinPacks(SqliteConnection c, SqliteTransaction tx, string name, string day)
    {
        var date = DateOnly.ParseExact(day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var start = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), DailyZone);
        var end = TimeZoneInfo.ConvertTimeToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue), DailyZone);
        int wins;
        using (var cmd = Tx(c, tx, """
            SELECT COUNT(DISTINCT json_extract(payload,'$.game.puzzle.givens')) FROM history
            WHERE name=$n AND outcome='completed'
            AND julianday(json_extract(payload,'$.ended')) >= julianday($start)
            AND julianday(json_extract(payload,'$.ended')) < julianday($end)
            AND NOT EXISTS(SELECT 1 FROM speed_audit a WHERE a.name=history.name AND a.id=history.id AND a.flagged=1)
            """, ("$n", name), ("$start", start.ToString("O")), ("$end", end.ToString("O"))))
            wins = Convert.ToInt32(cmd.ExecuteScalar());
        int existing;
        using (var count = Tx(c, tx, "SELECT COUNT(*) FROM daily_packs WHERE name=$n AND day=$d AND milestone<>'weekly'", ("$n", name), ("$d", day)))
            existing = Convert.ToInt32(count.ExecuteScalar());
        // Grandfather previously issued packs (including claimed ones) and count them
        // towards the new milestones, so an upgrade cannot issue duplicate rewards.
        (int Wins, string Key, int Amount)[] milestones = [(1, "win1", 1), (3, "win3", 1), (7, "win7", 3)];
        for (int i = existing; i < milestones.Length && wins >= milestones[i].Wins; i++)
            AddPack(c, tx, name, day, milestones[i].Key, 0, milestones[i].Amount);
        return wins;
    }
    private DailyStatus EnsureDaily(SqliteConnection c, SqliteTransaction tx, string name)
    {
        string day = Day(DateTimeOffset.UtcNow);
        SettleWeeks(c, tx);
        int wins = EnsureWinPacks(c, tx, name, day);
        var packs = new List<DailyPack>();
        using (var cmd = Tx(c, tx, "SELECT day,milestone,slot FROM daily_packs WHERE name=$n AND chosen IS NULL ORDER BY day,rowid", ("$n", name)))
        using (var rows = cmd.ExecuteReader())
            while (rows.Read()) packs.Add(new(rows.GetString(0), rows.GetString(1), rows.GetInt32(2)));
        var balances = new Dictionary<string,int>();
        using (var cmd = Tx(c, tx, "SELECT kind,amount FROM wallet WHERE name=$n", ("$n", name)))
        using (var rows = cmd.ExecuteReader()) while (rows.Read()) balances[rows.GetString(0)] = rows.GetInt32(1);
        return new(day, wins, balances.GetValueOrDefault("life"), balances.GetValueOrDefault("hint"), balances.GetValueOrDefault("candidates"), packs);
    }
    public DailyStatus Daily(string name)
    {
        lock (Gate)
        {
            using var c = db.Open(); using var tx = c.BeginTransaction();
            var status = EnsureDaily(c, tx, name); tx.Commit(); return status;
        }
    }
    public DailyResult? Choose(string name, DailyChoice choice)
    {
        if (choice.Card is < 0 or > 2) return null;
        lock (Gate)
        {
            using var c = db.Open(); using var tx = c.BeginTransaction();
            EnsureDaily(c, tx, name);
            var args = new (string, object?)[] { ("$n", name), ("$d", choice.Day), ("$m", choice.Milestone), ("$s", choice.Slot) };
            RewardItem item;
            int? chosen;
            using (var cmd = Tx(c, tx, "SELECT options,chosen FROM daily_packs WHERE name=$n AND day=$d AND milestone=$m AND slot=$s", args))
            using (var rows = cmd.ExecuteReader())
            {
                if (!rows.Read()) return null;
                chosen = rows.IsDBNull(1) ? null : rows.GetInt32(1);
                if (chosen is int previous && previous != choice.Card) return null;
                item = Database.Decode<RewardItem[]>(rows.GetString(0))[choice.Card];
            }
            if (chosen is null)
            {
                using var mark = Tx(c, tx, "UPDATE daily_packs SET chosen=$i WHERE name=$n AND day=$d AND milestone=$m AND slot=$s",
                    [.. args, ("$i", choice.Card)]);
                mark.ExecuteNonQuery();
                using var credit = Tx(c, tx, """
                    INSERT INTO wallet VALUES($n,$k,$a) ON CONFLICT(name,kind) DO UPDATE SET amount=amount+excluded.amount
                    """, ("$n", name), ("$k", item.Kind), ("$a", item.Amount));
                credit.ExecuteNonQuery();
            }
            var status = EnsureDaily(c, tx, name); tx.Commit(); return new(item, status);
        }
    }
    private static bool Debit(SqliteConnection c, SqliteTransaction tx, string name, string kind)
    {
        using var cmd = Tx(c, tx, "UPDATE wallet SET amount=amount-1 WHERE name=$n AND kind=$k AND amount>0",
            ("$n", name), ("$k", kind));
        return cmd.ExecuteNonQuery() == 1;
    }
    public bool HasWallet(string name, string kind)
    {
        using var c = db.Open(); using var cmd = Database.Command(c, "SELECT amount FROM wallet WHERE name=$n AND kind=$k", ("$n", name), ("$k", kind));
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0) > 0;
    }
    public WalletResult? SpendDesktop(string name, WalletRequest request)
    {
        if (request.Operation == Guid.Empty || request.GameId == Guid.Empty || request.Kind is not ("life" or "hint" or "candidates")) return null;
        lock (Gate)
        {
            using var c = db.Open(); using var tx = c.BeginTransaction();
            using (var get = Tx(c, tx, "SELECT game_id,kind FROM wallet_spends WHERE name=$n AND operation=$o",
                ("$n", name), ("$o", request.Operation.ToString())))
            using (var rows = get.ExecuteReader())
                if (rows.Read())
                    return rows.GetString(0) == request.GameId.ToString() && rows.GetString(1) == request.Kind
                        ? new(request.Operation, request.GameId, request.Kind) : null;
            if (request.Kind == "life" || !Debit(c, tx, name, request.Kind)) return null;
            using var add = Tx(c, tx, "INSERT INTO wallet_spends VALUES($n,$o,$g,$k)", ("$n", name),
                ("$o", request.Operation.ToString()), ("$g", request.GameId.ToString()), ("$k", request.Kind));
            add.ExecuteNonQuery(); tx.Commit(); return new(request.Operation, request.GameId, request.Kind);
        }
    }
}
