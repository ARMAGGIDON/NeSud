using System.Security.Cryptography;
using System.Text;
using NeSud.Core;
using NeSud.Storage;

namespace NeSud.Server;

public sealed partial class ServerStore
{
    public List<RankEntry> Rankings(bool all = false)
    {
        using var c = db.Open(); using var cmd = Database.Command(c, """
            SELECT h.name,COUNT(*),SUM(h.score),a.salt FROM history h JOIN accounts a ON a.name=h.name
            WHERE ($all=1 OR h.source='web') AND h.outcome='completed'
            AND NOT EXISTS(SELECT 1 FROM speed_audit v WHERE v.name=h.name AND v.id=h.id AND v.flagged=1)
            GROUP BY h.name,a.salt ORDER BY SUM(h.score) DESC,h.name LIMIT 100
            """, ("$all", all ? 1 : 0));
        using var rows = cmd.ExecuteReader(); var result = new List<RankEntry>();
        while (rows.Read()) result.Add(new(PublicName(rows.GetString(0), (byte[])rows[3]), rows.GetInt32(1), rows.GetInt64(2)));
        return result;
    }
    public DifficultyComparison Compare(Difficulty level, int score, int seconds, int rules = 3)
    {
        using var c = db.Open(); using var cmd = Database.Command(c, """
            SELECT h.name,h.score,json_extract(h.payload,'$.game.seconds'),json_extract(h.payload,'$.game.mistakes'),json_extract(h.payload,'$.game.hints'),coalesce(json_extract(h.payload,'$.game.candidateChanges'),0) FROM history h WHERE h.source='web' AND h.outcome='completed'
            AND json_extract(h.payload,'$.game.puzzle.difficulty')=$d
            AND coalesce(json_extract(h.payload,'$.game.rulesVersion'),0)=$r
            AND NOT EXISTS(SELECT 1 FROM speed_audit a WHERE a.name=h.name AND a.id=h.id AND a.flagged=1)
            """, ("$d", (int)level), ("$r", rules));
        using var rows = cmd.ExecuteReader();
        var records = new List<(int Score,int Seconds,int Mistakes,int Hints,long CandidateChanges)>(); var users = new HashSet<string>();
        while (rows.Read()) { users.Add(rows.GetString(0)); records.Add((rows.GetInt32(1),rows.GetInt32(2),rows.GetInt32(3),rows.GetInt32(4),rows.GetInt64(5))); }
        if (records.Count == 0) return new(0,0,0,0,0,0,0,0,0,0,0);
        var times = records.Select(r => r.Seconds).Order().ToArray(); int n = times.Length;
        double median = ((long)times[(n-1)/2]+times[n/2])/2.0;
        return new(n, users.Count, times.Average(), median, times[0], records.Average(r => r.Score),
            records.Average(r => r.Mistakes), records.Average(r => r.Hints), records.Average(r => r.CandidateChanges),
            (int)Math.Round(100.0 * records.Count(r => r.Score <= score)/n),
            (int)Math.Round(100.0 * times.Count(t => t >= seconds)/n));
    }
}
