using NeSud.Core;
using NeSud.Storage;
using NeSud.Server;
using System.Numerics;

namespace NeSud.SmokeTests;

internal static partial class SmokeTests
{
    private static void TestRewards(List<Puzzle> puzzles, Random random)
    {
    var dailyDir = Path.Combine(Path.GetTempPath(), "NeSud-daily-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var server = new ServerStore(Path.Combine(dailyDir, "server.db"));
            var user = server.Login(new Credentials("daily-user", "test-password-123", "test-password-123"), true)!.User;
            var other = server.Login(new Credentials("daily-other", "test-password-123", "test-password-123"), true)!.User;
            Check(server.Daily(user).Packs.Count == 0 && server.Daily(user).Packs.Count == 0, "No login reward");
            Check(server.Daily(other).Packs.Count == 0, "Account isolation");
            var ads = Game.Start(puzzles[0]);
            Check(!ads.CanAdReward("hint") && !ads.GrantReward("hint") && !ads.CanAdReward("candidates"), "No ad with available resource");
            Check(ads.FillNotes() && ads.CanAdReward("candidates") && ads.GrantReward("candidates") && !ads.GrantReward("candidates"), "Ads replenish only empty resource");
            var distinct = puzzles.DistinctBy(p => string.Concat(p.Givens)).ToList();
            while (distinct.Count < 8) distinct.Add(Sudoku.Generate(Difficulty.Easy, random: random));
            for (int n = 1; n <= 8; n++)
            {
                var solved = Game.Start(distinct[n - 1]);
                for (int j = 0; j < 81; j++) if (solved.Puzzle.Givens[j] == 0) solved.Enter(j, solved.Puzzle.Solution[j]);
                var now = DateTimeOffset.UtcNow;
                var entry = new GameRecord(Guid.NewGuid(), now, now, "desktop", "completed", solved);
                server.Upload(user, [entry]);
                server.Upload(user, [entry]);
                var daily = server.Daily(user);
                int count = n >= 7 ? 3 : n >= 3 ? 2 : 1;
                Check(daily.Wins == n && daily.Packs.Count == count, "Daily win milestones " + n);
            }
            var first = server.Daily(user);
            var pack = first.Packs[0];
            var choice = new DailyChoice(pack.Day, pack.Milestone, pack.Slot, 1);
            var result = server.Choose(user, choice)!;
            var again = server.Choose(user, choice)!;
            Check(result.Item == again.Item && (result.Item.Amount is 1 or 2) && result.Status.Packs.Count == 2 && again.Status.Packs.Count == 2 &&
                again.Status.Lives + again.Status.Hints + again.Status.Candidates == result.Item.Amount, "Idempotent card claim");
            Check(server.Choose(user, choice with { Card = 0 }) is null, "Cannot open another card");
            Check(server.Daily(other).Packs.Count == 0 && server.Daily(other).Lives + server.Daily(other).Hints + server.Daily(other).Candidates == 0, "Wallet isolation");
            var spend = new WalletRequest(Guid.NewGuid(), Guid.NewGuid(), result.Item.Kind);
            Check(server.SpendDesktop(user, spend) is not null && server.SpendDesktop(user, spend) is not null, "Idempotent spend");
            var balance = server.Daily(user);
            Check(balance.Lives + balance.Hints + balance.Candidates == result.Item.Amount - 1, "Debit once");
            var changes = server.HistorySince(user, 0);
            Check(changes.Records.Count == 8 && server.HistorySince(user, changes.Cursor).Records.Count == 0, "Incremental history");
            var aggregate = server.Accumulated("perfect", true).Single(r => r.User == user);
            Check(aggregate.Wins == 8 && aggregate.PerfectWins == 8 && aggregate.CurrentStreak == 8 && aggregate.BestStreak == 8, "Accumulated perfect wins and streak");
            Check(ServerStore.WeeklyPrize(1) == 3 && ServerStore.WeeklyPrize(3) == 3 && ServerStore.WeeklyPrize(4) == 2 &&
                ServerStore.WeeklyPrize(10) == 2 && ServerStore.WeeklyPrize(11) == 1 && ServerStore.WeeklyPrize(30) == 1 &&
                ServerStore.WeeklyPrize(31) == 0, "Weekly prize tiers");
            var previousDay = DateTimeOffset.UtcNow.AddDays(-1);
            var oldGame = Game.Start(distinct[0]);
            for (int j = 0; j < 81; j++) if (oldGame.Puzzle.Givens[j] == 0) oldGame.Enter(j, oldGame.Puzzle.Solution[j]);
            var late = new GameRecord(Guid.NewGuid(), previousDay, previousDay, "desktop", "completed", oldGame);
            server.Upload(other, [late]);
            server.Upload(other, [late]);
            var lateStatus = server.Daily(other);
            Check(lateStatus.Wins == 0 && lateStatus.Packs.Count == 1 &&
                lateStatus.Packs.Any(p => p.Milestone == "win1" && p.Day != lateStatus.Day), "Late offline completion credits original day");
            var week = server.Weekly();
            var previousWeek = DateOnly.ParseExact(week.Week, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).AddDays(-7);
            string prior = previousWeek.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var priorTime = new DateTimeOffset(previousWeek.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
            var webRecord = new GameRecord(Guid.NewGuid(), priorTime, priorTime, "web", "completed", oldGame);
            server.SaveSession(other, new WebGame(webRecord.Id, priorTime, oldGame, priorTime, 1), webRecord);
            var rankedUsers = new List<string> { other };
            for (int rank = 2; rank <= 11; rank++)
            {
                var name = server.Login(new Credentials($"weekly-rank-{rank:00}", "test-password-123", "test-password-123"), true)!.User;
                rankedUsers.Add(name);
                var solved = Database.Decode<Game > (Database.Encode(oldGame));
                solved.Seconds = rank * 60;
                var entry = new GameRecord(Guid.NewGuid(), priorTime, priorTime, "web", "completed", solved);
                server.SaveSession(name, new WebGame(entry.Id, priorTime, solved, priorTime, 1), entry);
            }
            using (var conn = new Database(Path.Combine(dailyDir, "server.db")).Open())
            using (var command = Database.Command(conn, "UPDATE tournament_state SET next_week=$w WHERE id=1", ("$w", prior)))
                command.ExecuteNonQuery();
            var closed = server.Weekly();
            Check(closed.PreviousLeaders.Select(r => r.User).SequenceEqual(rankedUsers), "Weekly settlement");
            for (int rank = 1; rank <= 11; rank++)
            {
                using var conn = new Database(Path.Combine(dailyDir, "server.db")).Open();
                using var command = Database.Command(conn, "SELECT (SELECT json_group_array(json_object('kind',kind,'amount',amount)) FROM (SELECT kind,amount FROM daily_pack_cards k WHERE (k.name,k.day,k.milestone,k.slot)=(p.name,p.day,p.milestone,p.slot) ORDER BY card)) FROM daily_packs p WHERE name=$n AND milestone=\'weekly\'", ("$n", rankedUsers[rank - 1]));
                var cards = Database.Decode<RewardItem[] > ((string) command.ExecuteScalar()!);
                int[] expected = rank <= 2?[3, 3, 3]: rank == 3?[2, 3, 3]: rank <= 10?[2, 2, 2]: [1, 1, 2];
                Check(cards.Select(x => x.Amount).Order().SequenceEqual(expected) && cards.Select(x => x.Kind).Distinct().Count() == 2, "Weekly distribution rank " + rank);
                int main = ServerStore.WeeklyPrize(rank);
                Check(cards.Any(x => x.Kind == "hint" && x.Amount == main) && cards.Any(x => x.Kind == "candidates" && x.Amount == main), "Both weekly base resources");
            }
            Check(server.Daily(other).Packs.Count(p => p.Milestone == "weekly") == 1, "Top three get one pack");
            server.Weekly();
            Check(server.Daily(other).Packs.Count(p => p.Milestone == "weekly") == 1, "Weekly award idempotency");
            var weeklyPack = server.Daily(other).Packs.Single(p => p.Milestone == "weekly");
            var weeklyChoice = new DailyChoice(weeklyPack.Day, weeklyPack.Milestone, weeklyPack.Slot, 2);
            var weeklyReward = server.Choose(other, weeklyChoice)!;
            Check(weeklyReward.Item.Amount == 3 && server.Choose(other, weeklyChoice)!.Item == weeklyReward.Item &&
                server.Choose(other, weeklyChoice with { Card = 1 }) is null, "Weekly guaranteed amount and single card");
            var weekUser = server.Login(new Credentials("reward-week", "test-password-123", "test-password-123"), true)!.User;
            var today = DateOnly.ParseExact(first.Day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");
            for (int day = 0; day < 7; day++)
            {
                // Use seven fully elapsed local calendar days to avoid crossing midnight during the test.
                var ended = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(today.AddDays(-day - 1).ToDateTime(new TimeOnly(12, 0)), zone));
                for (int n = 0; n < 7; n++)
                {
                    var solved = Game.Start(distinct[n]);
                    for (int j = 0; j < 81; j++) if (solved.Puzzle.Givens[j] == 0) solved.Enter(j, solved.Puzzle.Solution[j]);
                    var record = new GameRecord(Guid.NewGuid(), ended, ended, "desktop", "completed", solved);
                    server.Upload(weekUser, [record]);
                }
            }
            var earned = server.Daily(weekUser).Packs;
            Check(earned.Count == 21 && earned.GroupBy(p => p.Day).All(group => group.Count() == 3), "Seven days yield 21 daily packs");
            int units = 0;
            var bestCards = new Dictionary < (string, string, int), int > ();
            using (var conn = new Database(Path.Combine(dailyDir, "server.db")).Open())
            using (var command = Database.Command(conn, "SELECT day,milestone,slot,(SELECT json_group_array(json_object('kind',kind,'amount',amount)) FROM (SELECT kind,amount FROM daily_pack_cards k WHERE (k.name,k.day,k.milestone,k.slot)=(p.name,p.day,p.milestone,p.slot) ORDER BY card)) FROM daily_packs p WHERE name=$n", ("$n", weekUser)))
            using (var rows = command.ExecuteReader())
                while (rows.Read())
                {
                    var cards = Database.Decode<RewardItem[] > (rows.GetString(3));
                    int baseAmount = rows.GetString(1) == "win7"?3: 1;
                    int[] expected = baseAmount == 3?[3, 3, 3]: [1, 1, 2];
                    Check(cards.Select(x => x.Amount).Order().SequenceEqual(expected) && cards.Select(x => x.Kind).Distinct().Count() == 2, "Daily card distribution");
                    Check(cards.Any(x => x.Kind == "hint" && x.Amount == baseAmount) && cards.Any(x => x.Kind == "candidates" && x.Amount == baseAmount), "Both daily base resources");
                    bestCards[(rows.GetString(0), rows.GetString(1), rows.GetInt32(2))] = Array.FindIndex(cards, x => x.Amount == cards.Max(y => y.Amount));
                }
            foreach (var p in earned)
                units += server.Choose(weekUser, new DailyChoice(p.Day, p.Milestone, p.Slot, bestCards[(p.Day, p.Milestone, p.Slot)]))!.Item.Amount;
            var wallet = server.Daily(weekUser);
            Check(units == 49 && wallet.Hints + wallet.Candidates == 49 && wallet.Packs.Count == 0 &&
                units + weeklyReward.Item.Amount == 52, "Weekly maximum is 52 resources");
            Console.WriteLine("PASS daily milestones 1/3/7, mixed shuffled cards, both resource types, weekly prize tiers and 52-resource cap");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dailyDir)) Directory.Delete(dailyDir, true);
        }
    }
}
