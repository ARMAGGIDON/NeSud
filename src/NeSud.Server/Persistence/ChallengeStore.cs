using Microsoft.Data.Sqlite;
using NeSud.Core;
using NeSud.Storage;
using System.Globalization;
using System.Security.Cryptography;

namespace NeSud.Server;

public sealed partial class ServerStore
{
    private static bool ParseDate(string value, out DateOnly day) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
    private static string MonthKey(DateOnly day) => day.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    public bool ChallengeAllowed(string value) => ParseDate(value, out var date) &&
        MonthKey(date) == Day(DateTimeOffset.UtcNow)[..7] && string.CompareOrdinal(value, Day(DateTimeOffset.UtcNow)) <= 0;

    private static void MigrateChallengeHistory(SqliteConnection c, SqliteTransaction tx)
    {
        using var exists = Tx(c, tx, "SELECT 1 FROM sqlite_master WHERE type='table' AND name='challenge_attempts'");
        if (exists.ExecuteScalar() is null) return;
        // The old table is removed only after every record has been copied successfully.
        using var migrate = Tx(c, tx, """
            INSERT INTO history(name,id,source,score,outcome,payload)
            SELECT name,id,'web',CASE WHEN outcome='completed' THEN json_extract(payload,'$.score') ELSE 0 END,
                outcome,json_set(payload,'$.challengeDay',day)
            FROM challenge_attempts;
            DROP TABLE challenge_attempts;
            """);
        migrate.ExecuteNonQuery();
    }

    private Difficulty ChallengeDifficulty(string day)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        using (var insert = Tx(c, tx, "INSERT OR IGNORE INTO challenge_difficulties VALUES($d,$l)",
            ("$d", day), ("$l", (int)ChallengeRules.DifficultyForRoll(RandomNumberGenerator.GetInt32(3)))))
            insert.ExecuteNonQuery();
        using var get = Tx(c, tx, "SELECT difficulty FROM challenge_difficulties WHERE day=$d", ("$d", day));
        var result = (Difficulty)Convert.ToInt32(get.ExecuteScalar());
        tx.Commit();
        return result;
    }

    public Puzzle ChallengePuzzle(string day, CancellationToken token = default)
    {
        using (var c = db.Open())
        using (var cmd = Database.Command(c, "SELECT payload FROM challenge_puzzles WHERE day=$d", ("$d", day)))
            if (cmd.ExecuteScalar() is string existing) return Database.Decode<Puzzle > (existing);
        var puzzle = Sudoku.Generate(ChallengeDifficulty(day), token);
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        using (var add = Tx(conn, tx, "INSERT OR IGNORE INTO challenge_puzzles VALUES($d,$p)", ("$d", day), ("$p", Database.Encode(puzzle)))) add.ExecuteNonQuery();
        using var get = Tx(conn, tx, "SELECT payload FROM challenge_puzzles WHERE day=$d", ("$d", day));
        var saved = Database.Decode<Puzzle > ((string)get.ExecuteScalar()!);
        tx.Commit();
        return saved;
    }

    public bool ChallengeWon(string name, string day)
    {
        using var c = db.Open();
        using var cmd = Database.Command(c, "SELECT 1 FROM challenge_wins WHERE name=$n AND day=$d", ("$n", name), ("$d", day));
        return cmd.ExecuteScalar() is not null;
    }
    private static void PruneChallengeSessions(SqliteConnection c, SqliteTransaction tx)
    {
        string start = Day(DateTimeOffset.UtcNow)[..7] + "-01";
        using var cmd = Tx(c, tx, "DELETE FROM challenge_sessions WHERE day<$d", ("$d", start));
        cmd.ExecuteNonQuery();
    }
    private WebGame? ChallengeSession(string name, string day)
    {
        using var c = db.Open();
        using var cmd = Database.Command(c, "SELECT payload FROM challenge_sessions WHERE name=$n AND day=$d", ("$n", name), ("$d", day));
        return cmd.ExecuteScalar() is string payload ? Database.Decode<WebGame > (payload) : null;
    }
    private void SaveChallengeSession(string name, WebGame session, GameRecord? terminal, string? walletKind)
    {
        if (IsGuest(name)) throw new InvalidOperationException("Challenge requires an account.");
        string day = session.ChallengeDay!;
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        if (walletKind is not null && !Debit(c, tx, name, walletKind)) throw new InvalidOperationException("Недостатньо ресурсів.");
        if (terminal is not null)
        {
            var record = terminal with { ChallengeDay = day };
            if (Insert(c, tx, name, record) && record.Outcome == "completed")
            {
                var audit = session.Audit ?? new MoveAudit();
                string endedDay = Day(record.Ended);
                bool flagged = audit.Flag(record.Game) || endedDay[..7] != day[..7] ||
                    string.CompareOrdinal(endedDay, day) < 0;
                using (var log = Tx(c, tx, "INSERT OR IGNORE INTO speed_audit VALUES($n,$id,$m,$f,$flag)",
                    ("$n", name), ("$id", record.Id.ToString()), ("$m", audit.Moves),
                    ("$f", audit.FastIntervals), ("$flag", flagged ? 1 : 0)))
                    log.ExecuteNonQuery();

                if (!flagged)
                {
                    using var win = Tx(c, tx, "INSERT OR IGNORE INTO challenge_wins VALUES($n,$d)",
                        ("$n", name), ("$d", day));
                    if (win.ExecuteNonQuery() == 1)
                    {
                        AddPack(c, tx, name, day, "challenge", 0,
                            ChallengeRules.CreateRewardCards(RandomNumberGenerator.GetInt32));
                        var date = ParseDay(day);
                        string month = MonthKey(date);
                        using var cup = Tx(c, tx, """
                            INSERT OR IGNORE INTO monthly_cups(name,month)
                            SELECT $n,$m WHERE (SELECT COUNT(*) FROM challenge_wins
                                WHERE name=$n AND day >= $start AND day < $end)=$days
                            """, ("$n", name), ("$m", month), ("$start", month + "-01"),
                            ("$end", DateKey(new DateOnly(date.Year, date.Month, 1).AddMonths(1))),
                            ("$days", DateTime.DaysInMonth(date.Year, date.Month)));
                        cup.ExecuteNonQuery();
                    }
                }
            }
        }
        if (!ChallengeAllowed(day))
        {
            tx.Commit();
            return;
        }
        using var save = Tx(c, tx, "INSERT INTO challenge_sessions VALUES($n,$d,$p) ON CONFLICT(name,day) DO UPDATE SET payload=excluded.payload",
            ("$n", name), ("$d", day), ("$p", Database.Encode(session)));
        save.ExecuteNonQuery();
        tx.Commit();
    }
    private static List<string> Cups(SqliteConnection c, string name)
    {
        using var cmd = Database.Command(c, "SELECT month FROM monthly_cups WHERE name=$n ORDER BY month DESC", ("$n", name));
        using var rows = cmd.ExecuteReader();
        var cups = new List<string > ();
        while (rows.Read()) cups.Add(rows.GetString(0));
        return cups;
    }
    public ChallengeCalendar? Calendar(string name, string? month = null)
    {
        lock (Gate)
        {
            using var cleanup = db.Open();
            using var tx = cleanup.BeginTransaction();
            PruneChallengeSessions(cleanup, tx);
            tx.Commit();
        }
        string today = Day(DateTimeOffset.UtcNow);
        month ??= today[..7];
        if (!ParseDate(month + "-01", out var start) || string.CompareOrdinal(month, today[..7]) > 0 || start.Year < 2000) return null;
        string end = DateKey(start.AddMonths(1));
        var days = new List<ChallengeDay > ();
        using var c = db.Open();
        var states = new Dictionary<string, string > ();
        using (var cmd = Database.Command(c, """
            SELECT day,CASE WHEN EXISTS(SELECT 1 FROM challenge_wins w WHERE w.name=s.name AND w.day=s.day) THEN 'won'
              WHEN json_extract(payload,'$.game.completed')=1 OR json_extract(payload,'$.game.outOfLives')=1 THEN 'retry'
              ELSE 'started' END FROM challenge_sessions s WHERE name=$n AND day >= $start AND day < $end
            UNION ALL SELECT day,'won' FROM challenge_wins WHERE name=$n AND day >= $start AND day < $end
            """, ("$n", name), ("$start", DateKey(start)), ("$end", end)))
        using (var rows = cmd.ExecuteReader()) while (rows.Read()) states[rows.GetString(0)] = rows.GetString(1);
        for (var d = start; d<start.AddMonths(1); d = d.AddDays(1))
        {
            string key = DateKey(d);
            string status = states.GetValueOrDefault(key, "available");
            if (status != "won") status = string.CompareOrdinal(key, today) > 0?"future": month != today[..7]?"missed": status;
            days.Add(new(key, status));
        }
        return new(month, today, days, Cups(c, name));
    }
}
