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
            byte[] avatar = MakeAvatar();
            Check(server.SetAvatar(inviter.User, avatar) &&
                server.GetAvatar("renamed")!.SequenceEqual(avatar) &&
                server.Profile(inviter.User).Avatar is not null, "Square avatar storage and public lookup");
            byte[] damaged = (byte[])avatar.Clone();
            damaged[^1] ^= 1;
            Check(!server.SetAvatar(inviter.User, damaged), "Reject corrupt PNG CRC");
            Check(server.SetAvatar(inviter.User, null) && server.GetAvatar("renamed") is null, "Remove avatar");
            Console.WriteLine("PASS identity, verification, referral idempotence and avatar validation");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static byte[] MakeAvatar()
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void Chunk(string type, byte[] data)
        {
            byte[] name = System.Text.Encoding.ASCII.GetBytes(type);
            Span<byte> number = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
            output.Write(number);
            output.Write(name);
            output.Write(data);
            uint crc = uint.MaxValue;
            foreach (byte b in name.Concat(data))
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                    crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xEDB88320u);
            }
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
            output.Write(number);
        }
        byte[] header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), 256);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), 256);
        header[8] = 8;
        header[9] = 6;
        Chunk("IHDR", header);
        using var data = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(data, System.IO.Compression.CompressionLevel.Fastest, true))
            zlib.Write(new byte[256 * (1 + 256 * 4)]);
        Chunk("IDAT", data.ToArray());
        Chunk("IEND", []);
        return output.ToArray();
    }
}
