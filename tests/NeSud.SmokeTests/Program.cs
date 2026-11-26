using NeSud.Core;
using NeSud.Storage;
using NeSud.Server;
using System.Numerics;

static void Check(bool result, string message)
{
    if (!result) throw new Exception(message);
}

int repetitions = args.Length > 0 ? int.Parse(args[0]) : 3;
var random = new Random(2036);
var puzzles = new List<Puzzle>();
foreach (var level in Enum.GetValues<Difficulty>())
{
    for (int i = 0; i < repetitions; i++)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var puzzle = Sudoku.Generate(level, timeout.Token, random);
        var (min, max) = Sudoku.ClueBand(level);
        int clues = puzzle.Givens.Count(v => v != 0);
        Check(clues >= min && clues <= max, "Clue band");
        Check(Sudoku.IsConsistent(puzzle.Solution) && !puzzle.Solution.Contains(0), "Complete valid solution");
        Check(Sudoku.CountSolutions(puzzle.Givens, token: timeout.Token) == 1, "Unique puzzle");
        Check(Sudoku.MatchesDifficulty(puzzle.Givens,level,timeout.Token), "Logical strategy certificate");
        Check(Game.Start(puzzle).Valid(), "Initial game validation");
        if (level is Difficulty.Easy or Difficulty.Medium)
            Check(Sudoku.SolvableBySingles(puzzle.Givens), "Solvable by singles");
        if (level == Difficulty.Expert)
            Check(!Sudoku.SolvableBySingles(puzzle.Givens), "Expert exceeds singles");
        puzzles.Add(puzzle);
        Console.WriteLine($"PASS {level}: {clues} clues");
    }
}
Check(puzzles.Select(p => string.Join("", p.Givens)).Distinct().Count() == puzzles.Count, "Variety");
var g = Game.Start(puzzles[0]);
int cell = g.Selected, correct = g.Puzzle.Solution[cell];
int given = Array.FindIndex(g.Puzzle.Givens, v => v != 0);
Check(!g.Enter(given, 0), "Immutable given");
Check(g.Enter(cell, correct, note: true), "Note entry");
Check(g.Notes[cell] == 1 << correct, "Note mask");
Check(g.Enter(cell, correct), "Number entry");
Check(g.Notes[cell] == 0, "Clear notes after number");
Check(g.Undo() && g.Values[cell] == 0 && g.Notes[cell] != 0, "Undo notes");
Check(g.Redo() && g.Values[cell] == correct, "Redo number");
Check(g.Undo() && g.Enter(cell, correct % 9 + 1), "Branch history");
Check(g.RedoStack.Count == 0 && g.Mistakes == 1, "Clear redo and count mistake");
Check(g.GetHint() is { } h && h.Cell == cell && h.Digit == correct, "Corrective hint");
Check(g.Enter(cell, correct, hint: true) && g.Hints == 1, "Apply hint");
g.Paused = true;
Check(!g.Enter(cell, 0) && !g.Undo() && !g.FillNotes(), "Pause guard");
g.Paused = false;
Check(g.FillNotes(), "Fill candidates");
g.Seconds = 123;
g.NotesMode = true;
var folder = Path.Combine(Path.GetTempPath(), "NeSud-tests-" + Guid.NewGuid().ToString("N"));
try
{
    var path = Path.Combine(folder, "state.json");
    var store = new StateStore(path);
    var state = new AppState { Game = g, Dark = false, ShowMistakes = false };
    store.Save(state);
    var loaded = store.Load();
    Check(loaded.Game is { } restored && restored.Valid() && restored.Seconds == 123 &&
        restored.NotesMode && restored.Values.SequenceEqual(g.Values) &&
        restored.Notes.SequenceEqual(g.Notes) && restored.UndoStack.Count == g.UndoStack.Count &&
        !loaded.Dark && !loaded.ShowMistakes, "Persistence round trip");
    g.Seconds = 124;
    store.Save(state);
    File.WriteAllText(path, "broken");
    Check(store.Load().Game?.Seconds == 123, "Backup recovery");
}
finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
g.Restart();
Check(g.Values.SequenceEqual(g.Puzzle.Givens) && g.Seconds == 0 && g.UndoStack.Count == 0, "Restart");
for (int i = 0; i < 81; i++) if (g.Puzzle.Givens[i] == 0) g.Enter(i, g.Puzzle.Solution[i]);
Check(g.Completed && !g.Enter(cell, 0), "Completion");
using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try { Sudoku.Generate(Difficulty.Easy, cancelled.Token); throw new Exception("Cancellation ignored"); }
catch (OperationCanceledException) { }
Console.WriteLine("PASS game, history, persistence, recovery, completion, cancellation");

var dbDir = Path.Combine(Path.GetTempPath(), "NeSud-db-tests-" + Guid.NewGuid().ToString("N"));
try
{
    var local = new LocalStore(Path.Combine(dbDir, "client.db"), Path.Combine(dbDir, "legacy.json"));
    using (var connection = new Database(Path.Combine(dbDir, "client.db")).Open())
    using (var versionQuery = Database.Command(connection, "SELECT sqlite_version()"))
    {
        var version = Version.Parse((string)versionQuery.ExecuteScalar()!);
        Check(version >= new Version(3, 50, 2), "Native SQLite must include the CVE-2025-6965 fix");
        Console.WriteLine($"PASS native SQLite {version}");
    }
    var attempt = Game.Start(puzzles[0]);
    var appState = new AppState { Game = attempt, GameId = Guid.NewGuid(), Started = DateTimeOffset.UtcNow };
    var record = new GameRecord(appState.GameId, appState.Started, DateTimeOffset.UtcNow, "desktop", "abandoned", attempt);
    local.Save(appState, record, "account-a");
    local.Save(appState, record, "account-a");
    Check(local.History("account-a", true).Count == 1, "Idempotent local history");
    Check(local.History("account-b").Count == 0, "Account isolation");
    local.Merge("account-a", [record]);
    Check(local.History("account-a", true).Count == 0 && local.History("account-a").Count == 1, "Sync acknowledgment");
    Check(local.Load().GameId == appState.GameId, "SQLite current game");
    Console.WriteLine("PASS local SQLite, account isolation, history and synchronization");
}
finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(dbDir)) Directory.Delete(dbDir, true); }

var lifeGame = Game.Start(puzzles[0]);
var blanks = Enumerable.Range(0, 81).Where(i => lifeGame.Puzzle.Givens[i] == 0).ToArray();
int initialScore = lifeGame.CurrentScore;
for (int i = 0; i < 3; i++) Check(lifeGame.Enter(blanks[i], lifeGame.Puzzle.Solution[blanks[i]] % 9 + 1), "Wrong move");
Check(lifeGame.Lives == 0 && lifeGame.OutOfLives && lifeGame.Mistakes == 3, "Life exhaustion");
Check(!lifeGame.Undo() && !lifeGame.Redo() && !lifeGame.Enter(blanks[3], lifeGame.Puzzle.Solution[blanks[3]])
    && !lifeGame.FillNotes() && lifeGame.GetHint() is null, "Exhaustion blocks actions");
Check(lifeGame.CurrentScore == initialScore - 225, "Mistake score penalty");
var lossTime = lifeGame.ReviveDeadline!.Value.AddSeconds(-10);
Check(!lifeGame.GrantReward("life") && lifeGame.CanStartRevive(lossTime) && !lifeGame.CanStartRevive(lossTime.AddSeconds(10)), "Revival is limited to defeat and ten seconds");
Check(lifeGame.BeginRevive(lossTime) && !lifeGame.BeginRevive(lossTime) && !lifeGame.FinishRevive(lossTime.AddSeconds(7)), "Single ad attempt and early claim denied");
Check(lifeGame.FinishRevive(lossTime.AddSeconds(8)) && lifeGame.Lives == 1 && !lifeGame.OutOfLives &&
    lifeGame.CurrentScore == initialScore - 325, "Life restoration and score penalty");
Check(lifeGame.Undo() && lifeGame.Lives == 1 && lifeGame.Mistakes == 3, "Undo never refunds lives");
var aid = Game.Start(puzzles[0]);
int blank = blanks[0], correctMask = Sudoku.Candidates(aid.Values, blank);
int validDigit = BitOperations.TrailingZeroCount((uint)correctMask);
int invalidDigit = BitOperations.TrailingZeroCount((uint)(Sudoku.All & ~correctMask));
Check(aid.Enter(blank, validDigit, note: true) && aid.Enter(blank, invalidDigit, note: true), "Manual notes setup");
int expected = Enumerable.Range(0, 81).Sum(j => aid.Values[j] == 0
    ? BitOperations.PopCount((uint)(aid.Notes[j] ^ Sudoku.Candidates(aid.Values, j))) : 0);
Check(aid.CandidateDelta() == expected && aid.FillNotes() && aid.CandidateCredits == 0 &&
    aid.CandidateUses == 1 && aid.CandidateChanges == expected &&
    aid.CurrentScore == Math.Max(0, initialScore - 2 * expected), "Candidate additions and corrections");
Check(aid.CandidateDelta() == 0 && aid.GrantReward("candidates", advertisement: false), "Wallet credit");
Check(!aid.FillNotes() && aid.CandidateCredits == 1, "No-op costs nothing");
Check(aid.Undo() && aid.CandidateCredits == 1 && aid.CandidatePenalty == 2 * expected, "Undo never refunds aid");
var hm = aid.GetHint()!.Value;
Check(aid.Enter(hm.Cell, hm.Digit, hint: true) && aid.HintCredits == 0 && aid.GetHint() is null, "Hint exhaustion");
foreach (int j in blanks.Where(j => j != hm.Cell).Take(15)) Check(aid.Enter(j, aid.Puzzle.Solution[j]), "Manual progress");
Check(aid.HintCredits == 0 && aid.CandidateCredits == 1, "Removed old milestone rewards");
var copied = Database.Decode<Game>(Database.Encode(aid));
Check(copied.Valid() && copied.CandidateUses == 1 && copied.CandidateChanges == expected &&
    copied.CurrentScore == aid.CurrentScore, "Resource persistence");
var legacy = Game.Start(puzzles[0]); legacy.RulesVersion = 1; legacy.CandidateUses = 2;
int legacyScore = legacy.CurrentScore; legacy.UpgradeRules();
Check(legacy.RulesVersion == 3 && legacy.CurrentScore == legacyScore, "Legacy migration");
Console.WriteLine("PASS lives, aid, variable penalties, legacy migration, persistence");

var cancelledRevive = Game.Start(puzzles[0]);
Check(!cancelledRevive.CanStartRevive(DateTimeOffset.UtcNow) && !cancelledRevive.GrantReward("life"), "No proactive lives");
for (int j = 0; j < 3; j++) cancelledRevive.Enter(blanks[j], cancelledRevive.Puzzle.Solution[blanks[j]] % 9 + 1);
var startedRevive = cancelledRevive.ReviveDeadline!.Value.AddSeconds(-10);
Check(cancelledRevive.BeginRevive(startedRevive), "Start revival");
cancelledRevive.CancelRevive();
Check(cancelledRevive.FinalDefeat(startedRevive.AddSeconds(1)) &&
    !cancelledRevive.BeginRevive(startedRevive.AddSeconds(1)) && !cancelledRevive.FinishRevive(startedRevive.AddSeconds(8)), "Cancel consumes chance");
var auto = Game.Start(puzzles[0]);
foreach (int j in blanks.Take(blanks.Length-10)) auto.Enter(j, auto.Puzzle.Solution[j]);
auto.AutoFinish = true;
Check(!auto.TryAutoFinish(), "Auto finish not at ten blanks");
int tenth = blanks[blanks.Length-10];
Check(auto.Enter(tenth, auto.Puzzle.Solution[tenth]) && auto.Completed && auto.AutoFilled == 9 &&
    auto.AutoFillPenalty == 180 && auto.Seconds == 0, "Auto finish applies points not time");
Check(auto.Remaining(1) == 0 && auto.Remaining(9) == 0, "Digit counts on completion");
var manual = Game.Start(puzzles[0]);
foreach (int j in blanks) manual.Enter(j, manual.Puzzle.Solution[j]);
manual.Seconds = manual.TargetSeconds/2;
Check(manual.SpeedBonus == (int)Math.Round(manual.BaseScore*0.6*0.25), "Quadratic midpoint");
manual.Seconds = manual.TargetSeconds;
Check(manual.SpeedBonus == 0, "Target speed bonus zero");
var rapid = new MoveAudit();
var nowAudit = DateTimeOffset.UtcNow;
for (int j = 0; j < 20; j++) rapid = rapid.Record(nowAudit.AddMilliseconds(j*30));
Check(rapid.Flag(manual), "Private rapid move audit");
Console.WriteLine("PASS timed single revival, cancellation, auto finish, counts, quadratic bonus and audit");

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
        server.Upload(user, [entry]); server.Upload(user, [entry]);
        var daily = server.Daily(user);
        int count = n >= 7 ? 3 : n >= 3 ? 2 : 1;
        Check(daily.Wins == n && daily.Packs.Count == count, "Daily win milestones " + n);
    }
    var first = server.Daily(user);
    var pack = first.Packs[0]; var choice = new DailyChoice(pack.Day, pack.Milestone, pack.Slot, 1);
    var result = server.Choose(user, choice)!; var again = server.Choose(user, choice)!;
    Check(result.Item == again.Item && (result.Item.Amount is 1 or 2) && result.Status.Packs.Count == 2 && again.Status.Packs.Count == 2 &&
        again.Status.Lives + again.Status.Hints + again.Status.Candidates == result.Item.Amount, "Idempotent card claim");
    Check(server.Choose(user, choice with { Card = 0 }) is null, "Cannot open another card");
    Check(server.Daily(other).Packs.Count == 0 && server.Daily(other).Lives + server.Daily(other).Hints + server.Daily(other).Candidates == 0, "Wallet isolation");
    var spend = new WalletRequest(Guid.NewGuid(), Guid.NewGuid(), result.Item.Kind);
    Check(server.SpendDesktop(user, spend) is not null && server.SpendDesktop(user, spend) is not null, "Idempotent spend");
    var balance = server.Daily(user);
    Check(balance.Lives + balance.Hints + balance.Candidates == result.Item.Amount - 1, "Debit once");
    var changes = server.HistorySince(user, 0);
    Check(changes.Records.Count == 8 && server.HistorySince(user,changes.Cursor).Records.Count == 0, "Incremental history");
    var aggregate = server.Accumulated("perfect",true).Single(r=>r.User==user);
    Check(aggregate.Wins==8 && aggregate.PerfectWins==8 && aggregate.CurrentStreak==8 && aggregate.BestStreak==8, "Accumulated perfect wins and streak");
    Check(ServerStore.WeeklyPrize(1)==3 && ServerStore.WeeklyPrize(3)==3 && ServerStore.WeeklyPrize(4)==2 &&
        ServerStore.WeeklyPrize(10)==2 && ServerStore.WeeklyPrize(11)==1 && ServerStore.WeeklyPrize(30)==1 &&
        ServerStore.WeeklyPrize(31)==0, "Weekly prize tiers");
    var previousDay = DateTimeOffset.UtcNow.AddDays(-1);
    var oldGame = Game.Start(distinct[0]);
    for (int j = 0; j < 81; j++) if (oldGame.Puzzle.Givens[j] == 0) oldGame.Enter(j, oldGame.Puzzle.Solution[j]);
    var late = new GameRecord(Guid.NewGuid(), previousDay, previousDay, "desktop", "completed", oldGame);
    server.Upload(other, [late]); server.Upload(other, [late]);
    var lateStatus = server.Daily(other);
    Check(lateStatus.Wins == 0 && lateStatus.Packs.Count == 1 &&
        lateStatus.Packs.Any(p => p.Milestone == "win1" && p.Day != lateStatus.Day), "Late offline completion credits original day");
    var week = server.Weekly();
    var previousWeek = DateOnly.ParseExact(week.Week,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture).AddDays(-7);
    string prior = previousWeek.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
    var priorTime = new DateTimeOffset(previousWeek.ToDateTime(new TimeOnly(12,0)),TimeSpan.Zero);
    var webRecord = new GameRecord(Guid.NewGuid(),priorTime,priorTime,"web","completed",oldGame);
    server.SaveSession(other,new WebGame(webRecord.Id,priorTime,oldGame,priorTime,1),webRecord);
    var rankedUsers = new List<string> { other };
    for (int rank=2;rank<=11;rank++)
    {
        var name=server.Login(new Credentials($"weekly-rank-{rank:00}","test-password-123","test-password-123"),true)!.User;
        rankedUsers.Add(name);
        var solved=Database.Decode<Game>(Database.Encode(oldGame));
        solved.Seconds=rank*60;
        var entry=new GameRecord(Guid.NewGuid(),priorTime,priorTime,"web","completed",solved);
        server.SaveSession(name,new WebGame(entry.Id,priorTime,solved,priorTime,1),entry);
    }
    using (var conn = new Database(Path.Combine(dailyDir,"server.db")).Open())
    using (var command = Database.Command(conn,"UPDATE tournament_state SET next_week=$w WHERE id=1",("$w",prior)))
        command.ExecuteNonQuery();
    var closed = server.Weekly();
    Check(closed.PreviousLeaders.Select(r=>r.User).SequenceEqual(rankedUsers), "Weekly settlement");
    for(int rank=1;rank<=11;rank++)
    {
        using var conn=new Database(Path.Combine(dailyDir,"server.db")).Open();
        using var command=Database.Command(conn,"SELECT options FROM daily_packs WHERE name=$n AND milestone=\'weekly\'",("$n",rankedUsers[rank-1]));
        var cards=Database.Decode<RewardItem[]>((string)command.ExecuteScalar()!);
        int[] expected=rank<=2?[3,3,3]:rank==3?[2,3,3]:rank<=10?[2,2,2]:[1,1,2];
        Check(cards.Select(x=>x.Amount).Order().SequenceEqual(expected) && cards.Select(x=>x.Kind).Distinct().Count()==2, "Weekly distribution rank "+rank);
        int main=ServerStore.WeeklyPrize(rank);
        Check(cards.Any(x=>x.Kind=="hint" && x.Amount==main) && cards.Any(x=>x.Kind=="candidates" && x.Amount==main), "Both weekly base resources");
    }
    Check(server.Daily(other).Packs.Count(p=>p.Milestone=="weekly")==1, "Top three get one pack");
    server.Weekly(); Check(server.Daily(other).Packs.Count(p=>p.Milestone=="weekly")==1, "Weekly award idempotency");
    var weeklyPack = server.Daily(other).Packs.Single(p=>p.Milestone=="weekly");
    var weeklyChoice = new DailyChoice(weeklyPack.Day, weeklyPack.Milestone, weeklyPack.Slot, 2);
    var weeklyReward = server.Choose(other,weeklyChoice)!;
    Check(weeklyReward.Item.Amount==3 && server.Choose(other,weeklyChoice)!.Item==weeklyReward.Item &&
        server.Choose(other,weeklyChoice with { Card=1 }) is null, "Weekly guaranteed amount and single card");
    var weekUser = server.Login(new Credentials("reward-week", "test-password-123", "test-password-123"), true)!.User;
    var today = DateOnly.ParseExact(first.Day,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
    var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");
    for (int day=0;day<7;day++)
    {
        // Use seven fully elapsed local calendar days to avoid crossing midnight during the test.
        var ended = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(today.AddDays(-day-1).ToDateTime(new TimeOnly(12,0)),zone));
        for (int n=0;n<7;n++)
        {
            var solved = Game.Start(distinct[n]);
            for (int j=0;j<81;j++) if (solved.Puzzle.Givens[j]==0) solved.Enter(j,solved.Puzzle.Solution[j]);
            var record = new GameRecord(Guid.NewGuid(),ended,ended,"desktop","completed",solved);
            server.Upload(weekUser,[record]);
        }
    }
    var earned = server.Daily(weekUser).Packs;
    Check(earned.Count==21 && earned.GroupBy(p=>p.Day).All(group=>group.Count()==3), "Seven days yield 21 daily packs");
    int units=0;
    var bestCards=new Dictionary<(string,string,int),int>();
    using (var conn = new Database(Path.Combine(dailyDir,"server.db")).Open())
    using (var command = Database.Command(conn,"SELECT day,milestone,slot,options FROM daily_packs WHERE name=$n",("$n",weekUser)))
    using (var rows = command.ExecuteReader())
        while (rows.Read())
        {
            var cards=Database.Decode<RewardItem[]>(rows.GetString(3));
            int baseAmount=rows.GetString(1)=="win7"?3:1;
            int[] expected=baseAmount==3?[3,3,3]:[1,1,2];
            Check(cards.Select(x=>x.Amount).Order().SequenceEqual(expected) && cards.Select(x=>x.Kind).Distinct().Count()==2, "Daily card distribution");
            Check(cards.Any(x=>x.Kind=="hint" && x.Amount==baseAmount) && cards.Any(x=>x.Kind=="candidates" && x.Amount==baseAmount), "Both daily base resources");
            bestCards[(rows.GetString(0),rows.GetString(1),rows.GetInt32(2))]=Array.FindIndex(cards,x=>x.Amount==cards.Max(y=>y.Amount));
        }
    foreach(var p in earned)
        units+=server.Choose(weekUser,new DailyChoice(p.Day,p.Milestone,p.Slot,bestCards[(p.Day,p.Milestone,p.Slot)]))!.Item.Amount;
    var wallet = server.Daily(weekUser);
    Check(units==49 && wallet.Hints+wallet.Candidates==49 && wallet.Packs.Count==0 &&
        units+weeklyReward.Item.Amount==52, "Weekly maximum is 52 resources");
    Console.WriteLine("PASS daily milestones 1/3/7, mixed shuffled cards, both resource types, weekly prize tiers and 52-resource cap");
}
finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(dailyDir)) Directory.Delete(dailyDir, true); }

var challengeDir=Path.Combine(Path.GetTempPath(),"NeSud-challenge-tests-"+Guid.NewGuid().ToString("N"));
try
{
    string path=Path.Combine(challengeDir,"server.db");
    var server=new ServerStore(path);
    string user=server.Login(new Credentials("challenge-player","test-password-123","test-password-123"),true)!.User;
    string other=server.Login(new Credentials("challenge@example.com","test-password-123","test-password-123"),true)!.User;
    var cal=server.Calendar(user)!;
    string today=cal.Today;
    Check(server.ChallengeAllowed(today) && !server.ChallengeAllowed("9999-12-31") && !server.ChallengeAllowed("invalid"),"Challenge date validation");
    var common=server.ChallengePuzzle(today);
    Check(common.Givens.SequenceEqual(server.ChallengePuzzle(today).Givens),"Shared daily puzzle");
    var now=DateTimeOffset.UtcNow;
    var regular=new WebGame(Guid.NewGuid(),now,Game.Start(puzzles[0]),now,1);
    server.SaveSession(user,regular);
    var challenge=new WebGame(Guid.NewGuid(),now,Game.Start(common),now,1,ChallengeDay:today);
    server.SaveSession(user,challenge);
    Check(server.Session(user)!.Id==regular.Id && server.Session(user,today)!.Id==challenge.Id &&
        server.Session(other,today) is null,"Separate account and mode sessions");
    Game Solve(Puzzle puzzle)
    {
        var g=Game.Start(puzzle);
        for(int i=0;i<81;i++)if(puzzle.Givens[i]==0)g.Enter(i,puzzle.Solution[i]);
        return g;
    }
    var solved=Solve(common); challenge=challenge with {Game=solved};
    var terminal=new GameRecord(challenge.Id,now,now,"web","completed",solved);
    server.SaveSession(user,challenge,terminal);server.SaveSession(user,challenge,terminal);
    var rewards=server.Daily(user);
    Check(server.ChallengeWon(user,today) && rewards.Wins==0 && rewards.Packs.Count==1 && rewards.Packs[0].Milestone=="challenge" &&
        server.History(user).Count==0 && server.Calendar(user)!.Days.Single(d=>d.Day==today).Status=="won","Daily challenge reward once; regular history isolated");
    var pack=rewards.Packs[0]; var choice=new DailyChoice(pack.Day,pack.Milestone,pack.Slot,1);
    var result=server.Choose(user,choice)!;
    Check((result.Item.Amount is 1 or 2) && server.Choose(user,choice)!.Item==result.Item && server.Choose(user,choice with {Card=2}) is null,"Challenge choice idempotence");
    // Seed an entire leap-year February in a test-only database to exercise all calendar days.
    var zone=TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");
    for(int d=1;d<=29;d++)
    {
        string day=$"2024-02-{d:00}";
        using(var conn=new Database(path).Open())
        using(var add=Database.Command(conn,"INSERT INTO challenge_puzzles VALUES($d,$p)",("$d",day),("$p",Database.Encode(common))))add.ExecuteNonQuery();
        var ended=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(new DateTime(2024,2,d,12,0,0),zone));
        var g=Solve(common);var game=new WebGame(Guid.NewGuid(),ended,g,ended,1,ChallengeDay:day);
        var record=new GameRecord(game.Id,ended,ended,"web","completed",g);
        server.SaveSession(user,game,record);server.SaveSession(user,game,record);
        Check(server.Profile(user).Cups.Contains("2024-02")==(d==29),"Cup only after all calendar days");
    }
    var profile=server.Profile(user);
    Check(profile.Cups.Count==1 && profile.DailyWins==30 && profile.Games==0 && profile.History.Count==30 && profile.History.All(r=>r.Mode=="challenge"),"Public profile challenge stats");
    Check(server.Calendar(user,"2024-02")!.Days.All(d=>d.Status=="won") && !server.ChallengeAllowed("2024-02-29"),"Historical calendar read only");
    Check(server.Calendar(other,"2024-02")!.Days.All(d=>d.Status=="missed") && server.Profile(other).Cups.Count==0,"Cup account isolation");
    var masked=server.Profile(other).User;
    Check(!masked.Contains('@') && server.ResolvePublicName(masked)==other && server.ResolvePublicName(other) is null,"Masked profile lookup");
    var json=Database.Encode(profile);
    Check(!json.Contains("\"solution\"")&&!json.Contains("\"token\"")&&!json.Contains("\"payload\"")&&!json.Contains("\"flagged\""),"Public profile redaction");
    Console.WriteLine("PASS shared daily puzzle, independent challenge sessions, one reward per date, leap-month cup, public profiles");
}
finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(challengeDir))Directory.Delete(challengeDir,true);}

var socialDir=Path.Combine(Path.GetTempPath(),"NeSud-social-tests-"+Guid.NewGuid().ToString("N"));
try
{
    var path=Path.Combine(socialDir,"server.db");var server=new ServerStore(path);
    var a=server.Login(new Credentials("social-alice","test-password-123","test-password-123"),true)!.User;
    var b=server.Login(new Credentials("social-bob","test-password-123","test-password-123"),true)!.User;
    Check(server.FindPlayers("social-").Contains(a)&&server.FindPlayers("social-").Contains(b),"Nickname lookup");
    Check(!server.ChangeFriend(a,new(a,"send"))&&!server.ChangeFriend(a,new(b,"accept")),"No self friend or unsolicited acceptance");
    Check(server.ChangeFriend(a,new(b,"send"))&&!server.ChangeFriend(a,new(b,"send"))&&!server.ChangeFriend(b,new(a,"send")),"Unique pending request");
    Check(server.Friends(a).Single().Status=="outgoing"&&server.Friends(b).Single().Status=="incoming","Directed pending requests");
    Check(!server.ChangeFriend(a,new(b,"accept"))&&server.ChangeFriend(b,new(a,"decline"))&&server.Friends(a).Count==0,"Recipient decline only");
    Check(server.ChangeFriend(a,new(b,"send"))&&server.ChangeFriend(b,new(a,"accept"))&&server.Friends(a).Single().Status=="friend"&&server.Friends(b).Single().Status=="friend","Mutual friendship");
    Check(server.ChangeFriend(b,new(a,"remove"))&&server.Friends(a).Count==0&&server.Friends(b).Count==0,"Remove friendship");
    Check(server.ChangeFriend(a,new(b,"send"))&&server.ChangeFriend(a,new(b,"cancel")),"Cancel outgoing");
    var now=DateTimeOffset.UtcNow.AddMinutes(-10);
    var easy=puzzles.First(p=>p.Difficulty==Difficulty.Easy);var hard=puzzles.First(p=>p.Difficulty==Difficulty.Hard);
    int sequence=0;
    void Record(Puzzle p,bool win)
    {
        var g=Game.Start(p);
        if(win)for(int i=0;i<81;i++)if(p.Givens[i]==0)g.Enter(i,p.Solution[i]);
        var ended=now.AddSeconds(++sequence);
        server.Upload(a,[new GameRecord(Guid.NewGuid(),ended,ended,"desktop",win?"completed":"abandoned",g)]);
    }
    Record(easy,true);Record(hard,true);Record(easy,false);Record(hard,true);
    var profile=server.Profile(a);
    var e=profile.ByDifficulty!.Single(x=>x.Difficulty==Difficulty.Easy);var h=profile.ByDifficulty!.Single(x=>x.Difficulty==Difficulty.Hard);
    Check(profile.Wins==3&&e.Wins==1&&e.CurrentStreak==0&&h.Wins==2&&h.CurrentStreak==2&&h.BestStreak==2&&h.PerfectWins==2,"Difficulty statistics isolate streaks");
    Check(server.Profile(a,0,Difficulty.Hard).History.All(x=>x.Difficulty==Difficulty.Hard)&&
        server.Accumulated("wins",true,Difficulty.Hard).Single(x=>x.User==a).Wins==2,"Difficulty profile/rank filters");
    var oldDay="2024-01-01";var session=new WebGame(Guid.NewGuid(),now,Game.Start(easy),now,1,ChallengeDay:oldDay);
    using(var conn=new Database(path).Open())
    {
        using(var add=Database.Command(conn,"INSERT INTO challenge_puzzles VALUES($d,$p)",("$d",oldDay),("$p",Database.Encode(easy))))add.ExecuteNonQuery();
        using(var add=Database.Command(conn,"INSERT INTO challenge_sessions VALUES($n,$d,$p)",("$n",a),("$d",oldDay),("$p",Database.Encode(session))))add.ExecuteNonQuery();
    }
    server.Calendar(a);
    Check(server.Session(a,oldDay) is null,"Past month sessions pruned");
    Console.WriteLine("PASS logical difficulty certificates, nickname search, friendships, per-level statistics and month rollover");
}
finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(socialDir))Directory.Delete(socialDir,true);}
