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
            SELECT name FROM account_profiles WHERE username=$u LIMIT 1
            """, ("$u", user));
        return cmd.ExecuteScalar() as string;
    }
    public PublicProfile Profile(string name, int page = 0, Difficulty? difficulty = null)
    {
        using var c = db.Open();
        string label;
        bool verified, avatar;
        using (var cmd = Database.Command(c, "SELECT username,verified,avatar IS NOT NULL FROM account_profiles WHERE name=$n", ("$n", name)))
        using (var info = cmd.ExecuteReader())
        {
            info.Read();
            label = info.GetString(0);
            verified = info.GetBoolean(1);
            avatar = info.GetBoolean(2);
        }
        int games = 0, wins = 0, perfect = 0, streak = 0, best = 0, dailyWins = 0;
        long score = 0;
        var perLevel = Enum.GetValues<Difficulty > ().Select(d => new LevelStats(d, 0, 0, 0, 0, 0, 0)).ToArray();
        using (var cmd = Database.Command(c, """
            SELECT outcome,score,CASE WHEN outcome='completed' AND coalesce(json_extract(payload,'$.game.mistakes'),0)=0
              AND coalesce(json_extract(payload,'$.game.hints'),0)=0 AND coalesce(json_extract(payload,'$.game.candidateUses'),0)=0
              AND coalesce(json_extract(payload,'$.game.autoFilled'),0)=0 AND coalesce(json_extract(payload,'$.game.lifeRestores'),0)=0
              THEN 1 ELSE 0 END,json_extract(payload,'$.game.puzzle.difficulty'),coalesce(json_extract(payload,'$.game.seconds'),0) FROM history WHERE name=$n ORDER BY julianday(json_extract(payload,'$.ended')),id
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
                Score = old.Score + rows.GetInt64(1), CurrentStreak = current, BestStreak = Math.Max(old.BestStreak, current),
                BestSeconds = won ? Math.Min(old.BestSeconds ?? int.MaxValue, rows.GetInt32(4)) : old.BestSeconds };
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
        return new(label, games, wins, perfect, streak, best, score, dailyWins, Cups(c, name), history, more, perLevel.ToList(), verified, avatar ? "/api/avatar?user=" + Uri.EscapeDataString(label) : null);
    }
    public OwnProfileDetails OwnDetails(string name)
    {
        using var c = db.Open();
        long seconds, mistakes, hints;
        using (var cmd = Database.Command(c, """
            SELECT coalesce(sum(json_extract(payload,'$.game.seconds')),0),
                coalesce(sum(json_extract(payload,'$.game.mistakes')),0),
                coalesce(sum(json_extract(payload,'$.game.hints')),0)
            FROM history WHERE name=$n
            """, ("$n", name)))
        using (var rows = cmd.ExecuteReader())
        {
            rows.Read();
            seconds = rows.GetInt64(0);
            mistakes = rows.GetInt64(1);
            hints = rows.GetInt64(2);
        }
        var active = new List<ActiveGameInfo>();
        void Add(WebGame session)
        {
            if (session.Game.Completed || session.Game.FinalDefeat(DateTimeOffset.UtcNow)) return;
            active.Add(new(session.Id, "web", session.ChallengeDay, session.Game.Puzzle.Difficulty,
                session.Game.Seconds, session.LastSeen, session.Game.Paused));
        }
        if (Session(name) is { } regular) Add(regular);
        using (var cmd = Database.Command(c, "SELECT day,payload FROM challenge_sessions WHERE name=$n", ("$n", name)))
        using (var rows = cmd.ExecuteReader())
            while (rows.Read())
                if (ChallengeAllowed(rows.GetString(0))) Add(Database.Decode<WebGame>(rows.GetString(1)));
        foreach (var draft in Drafts(name))
            if (!draft.Game.Completed && !draft.Game.FinalDefeat(DateTimeOffset.UtcNow))
                active.Add(new(draft.Id, "desktop", null, draft.Game.Puzzle.Difficulty, draft.Game.Seconds, draft.Updated, draft.Game.Paused));
        return new(seconds, mistakes, hints, active.OrderByDescending(g => g.Updated).ToList());
    }
}
