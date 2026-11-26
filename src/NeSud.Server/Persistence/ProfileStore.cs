using Microsoft.Data.Sqlite;
using NeSud.Core;
using NeSud.Storage;
using System.Globalization;

namespace NeSud.Server;

public sealed partial class ServerStore
{
    public string? ResolvePublicName(string user)
    {
        if (user.Length > 80) return null;
        using var c = db.Open();
        using var cmd = Database.Command(c, """
            SELECT name FROM accounts WHERE (instr(name,'@')=0 AND name=lower($u))
              OR (instr(name,'@')>0 AND 'Гравець-'||substr(hex(salt),1,12)=$u) LIMIT 1
            """, ("$u", user));
        return cmd.ExecuteScalar() as string;
    }
    public PublicProfile Profile(string name, int page = 0, Difficulty? difficulty = null)
    {
        using var c = db.Open();
        string label;
        using (var cmd = Database.Command(c, "SELECT salt FROM accounts WHERE name=$n", ("$n", name)))
            label = PublicName(name, (byte[])cmd.ExecuteScalar()!);
        int games = 0, wins = 0, perfect = 0, streak = 0, best = 0, dailyWins = 0;
        long score = 0;
        var perLevel = Enum.GetValues<Difficulty > ().Select(d => new LevelStats(d, 0, 0, 0, 0, 0, 0)).ToArray();
        using (var cmd = Database.Command(c, """
            SELECT outcome,score,CASE WHEN outcome='completed' AND coalesce(json_extract(payload,'$.game.mistakes'),0)=0
              AND coalesce(json_extract(payload,'$.game.hints'),0)=0 AND coalesce(json_extract(payload,'$.game.candidateUses'),0)=0
              AND coalesce(json_extract(payload,'$.game.autoFilled'),0)=0 AND coalesce(json_extract(payload,'$.game.lifeRestores'),0)=0
              THEN 1 ELSE 0 END,json_extract(payload,'$.game.puzzle.difficulty') FROM history WHERE name=$n ORDER BY julianday(json_extract(payload,'$.ended')),id
            """, ("$n", name)))
        using (var rows = cmd.ExecuteReader()) while (rows.Read())
        {
            games++;
            score += rows.GetInt64(1);
            perfect += rows.GetInt32(2);
            bool won = rows.GetString(0) == "completed";
            if (won)
            {
                wins++;
                best = Math.Max(best,++ streak);
            }
            else streak = 0;
            int i = rows.GetInt32(3);
            if (i < 0 || i >= perLevel.Length)continue;
            var old = perLevel[i];
            int current = won?old.CurrentStreak + 1: 0;
            perLevel[i] = old with { Games = old.Games + 1, Wins = old.Wins + (won?1: 0), PerfectWins = old.PerfectWins + rows.GetInt32(2),
                Score = old.Score + rows.GetInt64(1), CurrentStreak = current, BestStreak = Math.Max(old.BestStreak, current) };
        }
        using (var cmd = Database.Command(c, "SELECT COUNT(*) FROM challenge_wins WHERE name=$n", ("$n", name))) dailyWins = Convert.ToInt32(cmd.ExecuteScalar());
        var history = new List<PublicGame > ();
        using (var cmd = Database.Command(c, """
            SELECT payload FROM history
            WHERE name=$n AND ($d IS NULL OR json_extract(payload,'$.game.puzzle.difficulty')=$d)
            ORDER BY julianday(json_extract(payload,'$.ended')) DESC,id LIMIT 51 OFFSET $offset
            """, ("$n", name), ("$offset", page * 50), ("$d", difficulty is null?null: (int)difficulty.Value)))
        using (var rows = cmd.ExecuteReader()) while (rows.Read())
        {
            var r = Database.Decode<GameRecord > (rows.GetString(0));
            var g = r.Game;
            string? day = r.ChallengeDay;
            history.Add(new(r.Ended, day is null?"regular": "challenge", r.Source, g.Puzzle.Difficulty, r.Outcome, g.Seconds, r.Score, g.Mistakes, g.Hints, g.CandidateUses, g.AutoFilled, g.LifeRestores, day));
        }
        bool more = history.Count > 50;
        if (more)history.RemoveAt(50);
        return new(label, games, wins, perfect, streak, best, score, dailyWins, Cups(c, name), history, more, perLevel.ToList());
    }
}
