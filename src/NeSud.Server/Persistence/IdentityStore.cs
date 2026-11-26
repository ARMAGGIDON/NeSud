using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using NeSud.Core;
using NeSud.Storage;

namespace NeSud.Server;

public sealed partial class ServerStore
{
    private static bool ValidUsername(string value) => value.Length is >= 3 and <= 32 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');

    private static bool ValidEmail(string value) => value.Length <= 254 &&
        System.Net.Mail.MailAddress.TryCreate(value, out var address) &&
        string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase) && address.Host.Contains('.');

    private static string NewInvite() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private static void MigrateProfiles(SqliteConnection c, SqliteTransaction tx)
    {
        var rows = new List<(string Name, byte[] Salt)>();
        using (var cmd = Tx(c, tx, "SELECT name,salt FROM accounts WHERE name NOT IN (SELECT name FROM account_profiles)"))
        using (var reader = cmd.ExecuteReader())
            while (reader.Read()) rows.Add((reader.GetString(0), (byte[])reader[1]));
        foreach (var row in rows)
        {
            using var insert = Tx(c, tx, "INSERT INTO account_profiles(name,username,email,invite) VALUES($n,$u,$e,$i)",
                ("$n", row.Name), ("$u", PublicName(row.Name, row.Salt)), ("$e", row.Name.Contains('@') ? row.Name : null), ("$i", NewInvite()));
            insert.ExecuteNonQuery();
        }
    }

    private static void EnsureLegacyProfile(SqliteConnection c, string name)
    {
        using var tx = c.BeginTransaction();
        MigrateProfiles(c, tx);
        tx.Commit();
    }

    private AccountSession? RegisterProfile(Credentials input)
    {
        string username = input.User.Trim().ToLowerInvariant(), email = input.Email!.Trim().ToLowerInvariant();
        string name = "id:" + Guid.NewGuid().ToString("N");
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Password(input.Password, salt);
        lock (Gate)
        {
            using var c = db.Open();
            using var tx = c.BeginTransaction();
            string? inviter = null;
            if (!string.IsNullOrWhiteSpace(input.Invite))
            {
                using var find = Tx(c, tx, "SELECT name FROM account_profiles WHERE invite=$i AND verified=1", ("$i", input.Invite));
                inviter = find.ExecuteScalar() as string;
            }
            try
            {
                using (var add = Tx(c, tx, "INSERT INTO accounts VALUES($n,$s,$h)", ("$n", name), ("$s", salt), ("$h", hash))) add.ExecuteNonQuery();
                using (var add = Tx(c, tx, "INSERT INTO account_profiles(name,username,email,invite,referred_by) VALUES($n,$u,$e,$i,$r)",
                    ("$n", name), ("$u", username), ("$e", email), ("$i", NewInvite()), ("$r", inviter))) add.ExecuteNonQuery();
                tx.Commit();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { return null; }
            return IssueSession(c, name);
        }
    }

    public AccountSettings Settings(string name)
    {
        using var c = db.Open();
        using var cmd = Database.Command(c, """
            SELECT username,email,verified,invite,avatar IS NOT NULL,
                (SELECT count(*) FROM referral_rewards WHERE inviter=$n)
            FROM account_profiles WHERE name=$n
            """, ("$n", name));
        using var rows = cmd.ExecuteReader();
        rows.Read();
        bool verified = rows.GetBoolean(2);
        return new(rows.GetString(0), rows.IsDBNull(1) ? null : rows.GetString(1), verified,
            verified ? rows.GetString(3) : null, rows.GetInt32(5), rows.GetBoolean(4));
    }

    public bool IsVerified(string name)
    {
        using var c = db.Open();
        using var cmd = Database.Command(c, "SELECT verified FROM account_profiles WHERE name=$n", ("$n", name));
        return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
    }

    public bool EditProfile(string name, ProfileEdit edit)
    {
        string requested = (edit.Username ?? "").Trim();
        string user = requested.ToLowerInvariant();
        string? email = string.IsNullOrWhiteSpace(edit.Email) ? null : edit.Email.Trim().ToLowerInvariant();
        if (email is not null && !ValidEmail(email)) return false;
        lock (Gate)
        {
            var current = Settings(name);
            bool rename = !string.Equals(requested, current.Username, StringComparison.Ordinal);
            if (rename && (!current.Verified || !ValidUsername(user))) return false;
            if (!rename) user = current.Username;
            using var c = db.Open();
            using var cmd = Database.Command(c, """
                UPDATE account_profiles SET username=$u, email=$e,
                    verification_hash=CASE WHEN email IS $e THEN verification_hash ELSE NULL END,
                    verification_expiry=CASE WHEN email IS $e THEN verification_expiry ELSE 0 END
                WHERE name=$n AND (verified=1 OR username=$u) AND (verified=0 OR email IS $e)
                """, ("$u", user), ("$e", email), ("$n", name));
            try { return cmd.ExecuteNonQuery() == 1; }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { return false; }
        }
    }

    public (string Email, string Code)? StartVerification(string name)
    {
        lock (Gate)
        {
            var settings = Settings(name);
            if (settings.Verified || settings.Email is null) return null;
            string code = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using var c = db.Open();
            using var cmd = Database.Command(c, """
                UPDATE account_profiles SET verification_hash=$h,verification_expiry=$expiry,verification_sent=$t
                WHERE name=$n AND verified=0 AND email=$e AND verification_sent <= $previous
                """, ("$h", TokenHash(code)), ("$expiry", now + 1800), ("$t", now), ("$n", name),
                ("$e", settings.Email), ("$previous", now - 60));
            return cmd.ExecuteNonQuery() == 1 ? (settings.Email, code) : null;
        }
    }

    public bool VerifyEmail(string name, string code)
    {
        if (code is null || code.Trim().Length != 16) return false;
        lock (Gate)
        {
            using var c = db.Open();
            using var tx = c.BeginTransaction();
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using (var verify = Tx(c, tx, """
                UPDATE account_profiles SET verified=1,verification_hash=NULL,verification_expiry=0
                WHERE name=$n AND email IS NOT NULL AND verified=0 AND verification_hash=$h AND verification_expiry>$t
                """, ("$n", name), ("$h", TokenHash(code.Trim().ToUpperInvariant())), ("$t", now)))
                if (verify.ExecuteNonQuery() != 1) return false;

            using var reward = Tx(c, tx, """
                INSERT OR IGNORE INTO referral_rewards(invited,inviter,rewarded_at)
                SELECT p.name,p.referred_by,$t FROM account_profiles p
                JOIN account_profiles inviter ON inviter.name=p.referred_by
                WHERE p.name=$n AND p.name<>inviter.name AND inviter.verified=1
                """, ("$n", name), ("$t", now));
            if (reward.ExecuteNonQuery() == 1)
            {
                using var credit = Tx(c, tx, """
                    INSERT INTO wallet(name,kind,amount)
                    SELECT inviter,'hint',3 FROM referral_rewards WHERE invited=$n
                    UNION ALL SELECT inviter,'candidates',3 FROM referral_rewards WHERE invited=$n
                    ON CONFLICT(name,kind) DO UPDATE SET amount=amount+excluded.amount
                    """, ("$n", name));
                credit.ExecuteNonQuery();
            }
            tx.Commit();
            return true;
        }
    }

    public string? RewardedInviter(string invited)
    {
        using var c = db.Open();
        using var cmd = Database.Command(c, "SELECT inviter FROM referral_rewards WHERE invited=$n", ("$n", invited));
        return cmd.ExecuteScalar() as string;
    }

    public bool SetAvatar(string name, byte[]? png)
    {
        if (png is null || !IsVerified(name)) return false;
        // Accept only bounded, canonical 256x256 PNG output, never original SVG markup.
        if (png.Length is < 33 or > 5 * 1024 * 1024 ||
            !png.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) ||
            !png.AsSpan(12, 4).SequenceEqual("IHDR"u8) ||
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16,4)) != 256 ||
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20,4)) != 256 || !ValidAvatarPng(png)) return false;
        using var c = db.Open();
        using var cmd = Database.Command(c, "UPDATE account_profiles SET avatar=$a WHERE name=$n AND verified=1", ("$a", png), ("$n", name));
        return cmd.ExecuteNonQuery() == 1;
    }

    private static bool ValidAvatarPng(byte[] png)
    {
        // Bound decompression and accept only conventional non-interlaced 8-bit RGB/RGBA.
        if (png[24] != 8 || png[25] is not (2 or 6) || png[26] != 0 || png[27] != 0 || png[28] != 0)
            return false;
        try
        {
            using var compressed = new MemoryStream();
            int offset = 8;
            bool header = false, end = false;
            while (offset <= png.Length - 12)
            {
                int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
                if (length < 0 || length > png.Length - offset - 12) return false;
                var type = png.AsSpan(offset + 4, 4);
                uint expectedCrc = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length, 4));
                if (PngCrc(png.AsSpan(offset + 4, length + 4)) != expectedCrc) return false;
                if (type.SequenceEqual("IHDR"u8))
                {
                    if (header || offset != 8 || length != 13) return false;
                    header = true;
                }
                else if (type.SequenceEqual("IDAT"u8))
                {
                    if (!header) return false;
                    compressed.Write(png, offset + 8, length);
                }
                else if (type.SequenceEqual("IEND"u8))
                {
                    end = length == 0 && offset + 12 == png.Length;
                    break;
                }
                else if (type.SequenceEqual("acTL"u8) || type.SequenceEqual("fcTL"u8) ||
                    type.SequenceEqual("fdAT"u8) || (type[0] & 32) == 0) return false;
                offset += 12 + length;
            }
            if (!header || !end || compressed.Length == 0) return false;
            compressed.Position = 0;
            using var inflate = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionMode.Decompress);
            var row = new byte[1 + 256 * (png[25] == 6 ? 4 : 3)];
            for (int y = 0; y < 256; y++)
            {
                inflate.ReadExactly(row);
                if (row[0] > 4) return false;
            }
            return inflate.ReadByte() == -1;
        }
        catch (InvalidDataException) { return false; }
        catch (IOException) { return false; }
    }

    private static uint PngCrc(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xEDB88320u);
        }
        return ~crc;
    }

    public byte[]? GetAvatar(string user)
    {
        using var c = db.Open();
        using var cmd = Database.Command(c, "SELECT avatar FROM account_profiles WHERE username=$u", ("$u", user));
        return cmd.ExecuteScalar() as byte[];
    }
}
