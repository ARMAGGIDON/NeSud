using NeSud.Core;
using NeSud.Server;

namespace NeSud.SmokeTests;

internal static partial class SmokeTests
{
    private static void TestIdentity()
    {
        string directory = Path.Combine(Path.GetTempPath(), "NeSud-identity-" + Guid.NewGuid().ToString("N"));
        try
        {
            var server = new ServerStore(Path.Combine(directory, "server.db"));
            const string password = "test-password-123";
            var inviter = server.Login(new("inviter", password, password, "inviter@example.test"), true)!;
            Check(!server.Settings(inviter.User).Verified && server.Settings(inviter.User).Invite is null, "Unverified invite disabled");
            var code = server.StartVerification(inviter.User)!.Value.Code;
            Check(!server.VerifyEmail(inviter.User, "0000000000000000") && server.VerifyEmail(inviter.User, code), "Correct verification");
            string invite = server.Settings(inviter.User).Invite!;
            var invited = server.Login(new("invited", password, password, "invited@example.test", invite), true)!;
            Check(server.Daily(inviter.User).Hints == 0 && server.Daily(inviter.User).Candidates == 0, "No reward before verification");
            var invitedCode = server.StartVerification(invited.User)!.Value.Code;
            Check(server.VerifyEmail(invited.User, invitedCode) && !server.VerifyEmail(invited.User, invitedCode), "Exactly-once verification");
            Check(server.Daily(inviter.User).Hints == 3 && server.Daily(inviter.User).Candidates == 3 &&
                server.Settings(inviter.User).Referrals == 1, "Atomic referral reward");
            Check(server.EditProfile(inviter.User, new("renamed", "inviter@example.test")), "Rename");
            Check(server.Authenticate(inviter.Token) == inviter.User && server.ResolvePublicName("renamed") == inviter.User &&
                server.ResolvePublicName("inviter") is null, "Stable identity");
            Check(server.Login(new("renamed", password), false)?.User == inviter.User &&
                server.Login(new("inviter@example.test", password), false)?.User == inviter.User &&
                server.Login(new("inviter", password), false) is null, "Username and email login");
            Check(server.Login(new("duplicate", password, password, "INVITER@example.test"), true) is null, "Unique email");
            Check(!server.EditProfile(inviter.User, new("renamed", "other@example.test")), "Verified email immutable");
            Check(!server.SetAvatar(inviter.User, new byte[64]), "Reject invalid avatar");
            Console.WriteLine("PASS identity, verification, referral idempotence and avatar validation");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
