using Microsoft.Data.Sqlite;
using NeSud.Core;
using NeSud.Storage;
using System.Globalization;

namespace NeSud.Server;

public sealed partial class ServerStore
{
    private static DateOnly ParseDay(string day) => DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string DateKey(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Week(DateTimeOffset now)
    {
        var day = ParseDay(Day(now));
        return DateKey(day.AddDays(-((int)day.DayOfWeek + 6) % 7));
    }
    private static string Boundary(string week) => TimeZoneInfo.ConvertTimeToUtc(ParseDay(week).ToDateTime(TimeOnly.MinValue), DailyZone).ToString("O");
    public static int WeeklyPrize(int rank) => rank is >= 1 and <= 3 ? 3 : rank is >= 4 and <= 10 ? 2 : rank is >= 11 and <= 30 ? 1 : 0;

    private static List < (string Name, int Wins, long Score, string Public)> WeekRows(SqliteConnection c, SqliteTransaction tx, string week)
    {
        string end = DateKey(ParseDay(week).AddDays(7));
        using var cmd = Tx(c, tx, """
            SELECT h.name,COUNT(*),SUM(h.score),a.username FROM history h JOIN account_profiles a ON a.name=h.name
            WHERE h.source='web' AND h.outcome='completed'
            AND h.ended_jd>=julianday($start)
            AND h.ended_jd<julianday($end)
            AND NOT EXISTS(SELECT 1 FROM speed_audit v WHERE v.name=h.name AND v.id=h.id AND v.flagged=1)
            GROUP BY h.name,a.username ORDER BY SUM(h.score) DESC,COUNT(*) DESC,h.name LIMIT 100
            """, ("$start", Boundary(week)), ("$end", Boundary(end)));
        using var rows = cmd.ExecuteReader();
        var result = new List < (string, int, long, string) > ();
        while (rows.Read()) result.Add((rows.GetString(0), rows.GetInt32(1), rows.GetInt64(2), rows.GetString(3)));
        return result;
    }

    private static void SettleWeeks(SqliteConnection c, SqliteTransaction tx)
    {
        string current = Week(DateTimeOffset.UtcNow);
        using var get = Tx(c, tx, "SELECT next_week FROM tournament_state WHERE id=1");
        string week = (string)get.ExecuteScalar()!;
        while (string.CompareOrdinal(week, current) < 0)
        {
            var leaders = WeekRows(c, tx, week);
            for (int i = 0; i<leaders.Count; i++)
            {
                var r = leaders[i];
                int rank = i + 1;
                using var save = Tx(c, tx, "INSERT OR IGNORE INTO tournament_results VALUES($w,$r,$n,$v,$s)",
                    ("$w", week), ("$r", rank), ("$n", r.Name), ("$v", r.Wins), ("$s", r.Score));
                save.ExecuteNonQuery();
                int amount = WeeklyPrize(rank);
                if (amount > 0) AddPack(c, tx, r.Name, week, "weekly", 0, amount, rank <= 2 ? 3 : 2);
            }
            week = DateKey(ParseDay(week).AddDays(7));
        }
        using var update = Tx(c, tx, "UPDATE tournament_state SET next_week=$w WHERE id=1 AND next_week<>$w", ("$w", week));
        update.ExecuteNonQuery();
    }

    public WeeklyRanking Weekly()
    {
        lock (Gate)
        {
            using var c = db.Open();
            using var tx = c.BeginTransaction();
            SettleWeeks(c, tx);
            string week = Week(DateTimeOffset.UtcNow), previous = DateKey(ParseDay(week).AddDays(-7));
            var leaders = WeekRows(c, tx, week).Select(r => new RankEntry(r.Public, r.Wins, r.Score)).ToList();
            var old = new List<RankEntry > ();
            using (var cmd = Tx(c, tx, "SELECT r.name,r.wins,r.score,a.username FROM tournament_results r JOIN account_profiles a ON a.name=r.name WHERE week=$w ORDER BY rank", ("$w", previous)))
            using (var rows = cmd.ExecuteReader()) while (rows.Read()) old.Add(new(rows.GetString(3), rows.GetInt32(1), rows.GetInt64(2)));
            tx.Commit();
            return new(week, leaders, previous, old);
        }
    }

    public List<AccumulatedRank> Accumulated(string metric, bool all, Difficulty? difficulty = null)
    {
        string order = metric switch { "perfect" => "perfect", "streak" => "best", "current" => "current", "wins" => "wins", _ => "score" };
        using var c = db.Open();
        using var cmd = Database.Command(c, """
            WITH ordered AS (
              SELECT h.name,h.id,h.score,h.outcome,
                h.perfect,
                ROW_NUMBER() OVER(PARTITION BY h.name ORDER BY h.ended_jd,h.id) seq,
                SUM(CASE WHEN h.outcome<>'completed' THEN 1 ELSE 0 END) OVER(PARTITION BY h.name ORDER BY h.ended_jd,h.id ROWS UNBOUNDED PRECEDING) grp
              FROM history h JOIN history_puzzles p USING(puzzle_id)
              WHERE ($all=1 OR h.source='web') AND ($d IS NULL OR p.difficulty=$d)
                AND NOT EXISTS(SELECT 1 FROM speed_audit v WHERE v.name=h.name AND v.id=h.id AND v.flagged=1)
            ), runs AS (
              SELECT name,grp,COUNT(*) wins,MAX(seq) last FROM ordered WHERE outcome='completed' GROUP BY name,grp
            ), totals AS (
              SELECT name,SUM(outcome='completed') wins,SUM(perfect) perfect,SUM(score) score,MAX(seq) last FROM ordered GROUP BY name
            ), stats AS (
              SELECT t.name,t.wins,t.perfect,t.score,coalesce(MAX(r.wins),0) best,
                coalesce(MAX(CASE WHEN r.last=t.last THEN r.wins END),0) current
              FROM totals t LEFT JOIN runs r ON r.name=t.name GROUP BY t.name
            )
            SELECT s.name,s.wins,s.perfect,s.current,s.best,s.score,a.username FROM stats s JOIN account_profiles a ON a.name=s.name
            ORDER BY
            """.Replace("($all=1 OR h.source='web')", all ? "1=1" : "h.source='web'")
                .Replace("($d IS NULL OR p.difficulty=$d)", difficulty is null ? "1=1" : "p.difficulty=$d")
                + " " + order + " DESC,s.score DESC,s.name LIMIT 100", difficulty is null ? [] : [("$d", (int)difficulty.Value)]);
        using var rows = cmd.ExecuteReader();
        var result = new List<AccumulatedRank > ();
        while (rows.Read()) result.Add(new(rows.GetString(6), rows.GetInt64(1), rows.GetInt64(2), rows.GetInt64(3), rows.GetInt64(4), rows.GetInt64(5)));
        return result;
    }
}
