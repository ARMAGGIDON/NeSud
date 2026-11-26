using NeSud.Core;
using NeSud.Storage;
using NeSud.Server;
using System.Numerics;

namespace NeSud.SmokeTests;

internal static partial class SmokeTests
{
    private static void TestChallenges(List<Puzzle> puzzles)
    {
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
    }
}
