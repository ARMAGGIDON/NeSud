using NeSud.Core;
using NeSud.Storage;
using NeSud.Server;
using System.Numerics;

namespace NeSud.SmokeTests;

internal static partial class SmokeTests
{
    private static void TestChallenges(List<Puzzle> puzzles)
    {
        var challengeDir = Path.Combine(Path.GetTempPath(), "NeSud-challenge-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(challengeDir, "server.db");
            var server = new ServerStore(path);
            string user = server.Login(new Credentials("challenge-player", "test-password-123", "test-password-123"), true)!.User;
            string other = server.Login(new Credentials("challenge@example.com", "test-password-123", "test-password-123"), true)!.User;
            var cal = server.Calendar(user)!;
            string today = cal.Today;
            Check(server.ChallengeAllowed(today) && !server.ChallengeAllowed("9999-12-31") && !server.ChallengeAllowed("invalid"), "Challenge date validation");
            Check(Enumerable.Range(0, 3).Select(ChallengeRules.DifficultyForRoll)
                .SequenceEqual(new[] { Difficulty.Medium, Difficulty.Hard, Difficulty.Expert }), "Daily difficulty distribution");
            var quantities = Enumerable.Range(0, 100).Select(ChallengeRules.RewardQuantity).ToArray();
            Check(quantities.Count(x => x == 1) == 65 && quantities.Count(x => x == 2) == 30 &&
                quantities.Count(x => x == 3) == 5, "Exact reward quantity probabilities");
            for (int roll = 0; roll < 100; roll++)
            {
                int call = 0;
                var cards = ChallengeRules.CreateRewardCards(max => call++ == 0 ? roll : max - 1);
                Check(cards.Length == 3 && cards.All(x => x.Amount == 3) &&
                    cards.Select(x => x.Kind).Distinct().Count() == (roll < 5 ? 1 : 2), "Single-type pack probability");
            }
            // A rare homogeneous pack can contain three different quantities of the same item.
            var rolls = new Queue<int>(new[] { 0, 0, 0, 65, 95, 2, 1 });
            var mixed = ChallengeRules.CreateRewardCards(_ => rolls.Dequeue());
            Check(mixed.All(x => x.Kind == "hint") &&
                mixed.Select(x => x.Amount).Order().SequenceEqual(new[] { 1, 2, 3 }), "Mixed quantities in homogeneous pack");
            var common = server.ChallengePuzzle(today);
            Check(common.Givens.SequenceEqual(server.ChallengePuzzle(today).Givens), "Shared daily puzzle");
            var now = DateTimeOffset.UtcNow;
            var regular = new WebGame(Guid.NewGuid(), now, Game.Start(puzzles[0]), now, 1);
            server.SaveSession(user, regular);
            var challenge = new WebGame(Guid.NewGuid(), now, Game.Start(common), now, 1, ChallengeDay: today);
            server.SaveSession(user, challenge);
            Check(server.Session(user)!.Id == regular.Id && server.Session(user, today)!.Id == challenge.Id &&
                server.Session(other, today) is null, "Separate account and mode sessions");
            var draft = new DesktopDraft(Guid.NewGuid(), now, now, Game.Start(puzzles[0]));
            Check(server.SaveDraft(user, draft), "Private desktop draft");
            var active = server.OwnDetails(user);
            Check(active.Active.Count == 3 && active.Active.Any(x => x.Id == regular.Id) &&
                active.Active.Any(x => x.Id == challenge.Id && x.ChallengeDay == today) &&
                active.Active.Any(x => x.Id == draft.Id && x.Source == "desktop") &&
                server.OwnDetails(other).Active.Count == 0, "Private active-game isolation");
            Game Solve(Puzzle puzzle)
            {
                var g = Game.Start(puzzle);
                for (int i = 0; i < 81; i++) if (puzzle.Givens[i] == 0) g.Enter(i, puzzle.Solution[i]);
                return g;
            }
            var solved = Solve(common);
            challenge = challenge with { Game = solved };
            var terminal = new GameRecord(challenge.Id, now, now, "web", "completed", solved);
            server.SaveSession(user, challenge, terminal);
            server.SaveSession(user, challenge, terminal);
            Check(server.OwnDetails(user).Active.All(x => x.Id != challenge.Id), "Completed challenge excluded from active games");
            var rewards = server.Daily(user);
            Check(server.ChallengeWon(user, today) && rewards.Wins == 0 && rewards.Packs.Count == 1 && rewards.Packs[0].Milestone == "challenge" &&
                server.History(user).Count == 1 && server.History(user)[0].ChallengeDay == today && server.Calendar(user)!.Days.Single(d => d.Day == today).Status == "won", "Daily challenge reward once; shared history");
            Check(common.Difficulty != Difficulty.Easy && server.ChallengePuzzle(today).Difficulty == common.Difficulty,
                "Stable random daily difficulty");
            Check(server.HistorySince(user, 0).Records.Single().ChallengeDay == today, "Challenge incremental history");
            Check(server.Weekly().Leaders.Single(r => r.User == user).Games == 1, "Challenge included in current weekly ranking");
            Check(server.Accumulated("wins", false, common.Difficulty).Single(r => r.User == user).Wins == 1,
                "Challenge included in difficulty ranking");
            var pack = rewards.Packs[0];
            var choice = new DailyChoice(pack.Day, pack.Milestone, pack.Slot, 1);
            var result = server.Choose(user, choice)!;
            Check((result.Item.Amount is >= 1 and <= 3) && server.Choose(user, choice)!.Item == result.Item && server.Choose(user, choice with { Card = 2 }) is null, "Challenge choice idempotence");
            // Seed an entire leap-year February in a test-only database to exercise all calendar days.
            var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");
            for (int d = 1; d <= 29; d++)
            {
                string day = $"2024-02-{d:00}";
                using (var conn = new Database(path).Open())
                using (var add = Database.Command(conn, "INSERT INTO challenge_puzzles VALUES($d,$p)", ("$d", day), ("$p", Database.Encode(common)))) add.ExecuteNonQuery();
                var ended = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(new DateTime(2024, 2, d, 12, 0, 0), zone));
                var g = Solve(common);
                var game = new WebGame(Guid.NewGuid(), ended, g, ended, 1, ChallengeDay: day);
                var record = new GameRecord(game.Id, ended, ended, "web", "completed", g);
                server.SaveSession(user, game, record);
                server.SaveSession(user, game, record);
                Check(server.Profile(user).Cups.Contains("2024-02") == (d == 29), "Cup only after all calendar days");
            }
            var profile = server.Profile(user);
            Check(profile.Cups.Count == 1 && profile.DailyWins == 30 && profile.Games == 30 && profile.Wins == 30 && profile.History.Count == 30 && profile.History.All(r => r.Mode == "challenge"), "Public profile challenge stats");
            Check(server.Calendar(user, "2024-02")!.Days.All(d => d.Status == "won") && !server.ChallengeAllowed("2024-02-29"), "Historical calendar read only");
            Check(server.Calendar(other, "2024-02")!.Days.All(d => d.Status == "missed") && server.Profile(other).Cups.Count == 0, "Cup account isolation");
            var masked = server.Profile(other).User;
            Check(!masked.Contains('@') && server.ResolvePublicName(masked) == other && server.ResolvePublicName(other) is null, "Masked profile lookup");
            var json = Database.Encode(profile);
            Check(!json.Contains("\"solution\"") && !json.Contains("\"token\"") && !json.Contains("\"payload\"") && !json.Contains("\"flagged\""), "Public profile redaction");
            // Recreate the previous schema with one legacy result to verify the upgrade path.
            string legacyDay = "2024-02-01";
            var legacyGame = Solve(common);
            var legacyEnd = new DateTimeOffset(2024, 2, 1, 12, 0, 0, TimeSpan.Zero);
            var legacyRecord = new GameRecord(Guid.NewGuid(), legacyEnd, legacyEnd, "web", "completed", legacyGame);
            using (var c = new Database(path).Open())
            {
                using (var create = Database.Command(c, """
                    CREATE TABLE challenge_attempts (
                        name TEXT NOT NULL REFERENCES accounts(name), id TEXT NOT NULL,
                        day TEXT NOT NULL REFERENCES challenge_puzzles(day), outcome TEXT NOT NULL,
                        ended TEXT NOT NULL, payload TEXT NOT NULL, PRIMARY KEY(name,id));
                    """)) create.ExecuteNonQuery();
                using var insert = Database.Command(c, "INSERT INTO challenge_attempts VALUES($n,$id,$d,$o,$e,$p)",
                    ("$n", other), ("$id", legacyRecord.Id.ToString()), ("$d", legacyDay),
                    ("$o", legacyRecord.Outcome), ("$e", legacyEnd.ToString("O")), ("$p", Database.Encode(legacyRecord)));
                insert.ExecuteNonQuery();
            }
            var migrated = new ServerStore(path);
            var migratedRecord = migrated.History(other).Single();
            Check(migratedRecord.Id == legacyRecord.Id && migratedRecord.ChallengeDay == legacyDay &&
                migratedRecord.Score == legacyRecord.Score, "Legacy challenge history migration");
            Check(new ServerStore(path).History(other).Count == 1, "Migration is idempotent");
            Console.WriteLine("PASS shared daily puzzle, isolated sessions, weighted rewards, unified statistics, migration, monthly cups");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(challengeDir)) Directory.Delete(challengeDir, true);
        }
    }
}
