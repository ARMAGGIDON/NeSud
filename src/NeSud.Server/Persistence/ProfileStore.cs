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
            SELECT outcome,score,perfect,p.difficulty,seconds FROM history h
            JOIN history_puzzles p USING(puzzle_id) WHERE name=$n ORDER BY ended_jd,id
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
            SELECT h.ended,h.challenge_day,h.source,p.difficulty,h.outcome,h.seconds,h.score,h.mistakes,h.hints,
                coalesce(json_extract(h.game,'$.candidateUses'),0),coalesce(json_extract(h.game,'$.autoFilled'),0),
                coalesce(json_extract(h.game,'$.lifeRestores'),0)
            FROM history h JOIN history_puzzles p USING(puzzle_id)
            WHERE h.name=$n AND ($d IS NULL OR p.difficulty=$d)
            ORDER BY h.ended_jd DESC,h.id LIMIT 51 OFFSET $offset
            """.Replace("($d IS NULL OR p.difficulty=$d)", difficulty is null ? "1=1" : "p.difficulty=$d"),
            difficulty is null ? [("$n", name), ("$offset", page * 50)] :
                [("$n", name), ("$offset", page * 50), ("$d", (int)difficulty.Value)]))
        using (var rows = cmd.ExecuteReader()) while (rows.Read())
        {
            string? day = rows.IsDBNull(1) ? null : rows.GetString(1);
            history.Add(new(DateTimeOffset.Parse(rows.GetString(0), CultureInfo.InvariantCulture),
                day is null ? "regular" : "challenge", rows.GetString(2), (Difficulty)rows.GetInt32(3),
                rows.GetString(4), rows.GetInt32(5), rows.GetInt32(6), rows.GetInt32(7), rows.GetInt32(8),
                rows.GetInt32(9), rows.GetInt32(10), rows.GetInt32(11), day));
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
            SELECT coalesce(sum(seconds),0),coalesce(sum(mistakes),0),coalesce(sum(hints),0)
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
                session.Game.Seconds, session.LastSeen, session.Game.Paused || (DateTimeOffset.UtcNow - session.LastSeen).TotalSeconds > 12));
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
