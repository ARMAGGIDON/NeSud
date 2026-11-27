using System.Security.Cryptography;
using System.Text;
using NeSud.Core;
using NeSud.Storage;

namespace NeSud.Server;

public sealed partial class ServerStore
{
    public List<RankEntry> Rankings(bool all = false)
    {
        using var c = db.Open();
        using var cmd = Database.Command(c, """
            SELECT h.name,COUNT(*),SUM(h.score),a.username FROM history h JOIN account_profiles a ON a.name=h.name
            WHERE h.outcome='completed' AND ($all=1 OR h.source='web')
            AND NOT EXISTS(SELECT 1 FROM speed_audit v WHERE v.name=h.name AND v.id=h.id AND v.flagged=1)
            GROUP BY h.name,a.username ORDER BY SUM(h.score) DESC,h.name LIMIT 100
            """.Replace("($all=1 OR h.source='web')", all ? "1=1" : "h.source='web'"));
        using var rows = cmd.ExecuteReader();
        var result = new List<RankEntry > ();
        while (rows.Read()) result.Add(new(rows.GetString(3), rows.GetInt32(1), rows.GetInt64(2)));
        return result;
    }
    public DifficultyComparison Compare(Difficulty level, int score, int seconds, int rules = 3)
    {
        using var c = db.Open();
        using var cmd = Database.Command(c, """
            WITH eligible AS (
                SELECT h.name,h.score,h.seconds,h.mistakes,h.hints,h.candidate_changes
                FROM history h JOIN history_puzzles p USING(puzzle_id)
                WHERE h.source='web' AND h.outcome='completed' AND p.difficulty=$d AND h.rules=$r
                AND NOT EXISTS(SELECT 1 FROM speed_audit a WHERE a.name=h.name AND a.id=h.id AND a.flagged=1)
            ), ordered AS (
                SELECT *,row_number() OVER (ORDER BY seconds) AS pos,count(*) OVER () AS n FROM eligible
            )
            SELECT count(*),count(DISTINCT name),avg(seconds),
                avg(CASE WHEN pos IN ((n+1)/2,(n+2)/2) THEN seconds END),
                min(seconds),avg(score),avg(mistakes),avg(hints),avg(candidate_changes),
                sum(score<=$score),sum(seconds>=$seconds)
            FROM ordered
            """, ("$d", (int)level), ("$r", rules), ("$score", score), ("$seconds", seconds));
        using var rows = cmd.ExecuteReader();
        rows.Read();
        int n = rows.GetInt32(0);
        if (n == 0) return new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        return new(n, rows.GetInt32(1), rows.GetDouble(2), rows.GetDouble(3), rows.GetInt32(4),
            rows.GetDouble(5), rows.GetDouble(6), rows.GetDouble(7), rows.GetDouble(8),
            (int)Math.Round(100.0 * rows.GetInt64(9) / n), (int)Math.Round(100.0 * rows.GetInt64(10) / n));
    }
}
