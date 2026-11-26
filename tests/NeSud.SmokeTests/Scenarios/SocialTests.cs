using NeSud.Core;
using NeSud.Storage;
using NeSud.Server;
using System.Numerics;

namespace NeSud.SmokeTests;

internal static partial class SmokeTests
{
    private static void TestSocial(List<Puzzle> puzzles)
    {
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
    }
}
