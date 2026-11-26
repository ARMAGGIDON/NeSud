using System.Security.Cryptography;
using System.Text;
using NeSud.Core;
using NeSud.Storage;

namespace NeSud.Server;

public sealed partial class ServerStore
{
    private readonly Dictionary<string, (DateTimeOffset Expires, WebGame? Game)> guests = new();
    private const string GuestPrefix = ":guest:";
    public static bool IsGuest(string name) => name.StartsWith(GuestPrefix, StringComparison.Ordinal);
    public AccountSession? Guest()
    {
        lock (Gate)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var expired in guests.Where(x => x.Value.Expires <= now).Select(x => x.Key).ToArray())
                guests.Remove(expired);
            if (guests.Count >= 500) return null;
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            guests[GuestPrefix + TokenHash(token)] = (now.AddHours(2), null);
            return new(token, "Гість");
        }
    }
    public string? WebUser(string token)
    {
        if (token.Length != 64) return null;
        string key = GuestPrefix + TokenHash(token);
        lock (Gate)
        {
            if (guests.TryGetValue(key, out var entry) && entry.Expires > DateTimeOffset.UtcNow)
            { guests[key] = (DateTimeOffset.UtcNow.AddHours(2), entry.Game);
            return key; }
        }
        return Authenticate(token);
    }
    public static string? ValidateCredentials(Credentials input, bool register)
    {
        if (input.User is null || input.Password is null) return "Введіть логін і пароль.";
        string name = input.User.Trim().ToLowerInvariant();
        bool username = name.Length is >= 3 and <= 32 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');
        bool email = name.Length <= 254 && System.Text.RegularExpressions.Regex.IsMatch(name,
            @"^[a-z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-z0-9](?:[a-z0-9-]*[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)+$");
        if (!username && !email) return "Введіть коректний логін (3–32 символи) або email.";
        if (input.Password.Length is < 10 or > 128) return "Пароль має містити від 10 до 128 символів.";
        if (register && input.Email is not null && (!username || !ValidEmail(input.Email))) return "Введіть окремий нікнейм і коректну електронну пошту.";
        if (register && input.Password != input.Confirmation) return "Паролі не збігаються.";
        return null;
    }
    private static string PublicName(string name, byte[] salt) => name.Contains('@')
        ? "Гравець-" + Convert.ToHexString(salt)[..12]
        : name;
    private static byte[] Password(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000, HashAlgorithmName.SHA256, 32);
    private static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public AccountSession? Login(Credentials input, bool register)
    {
        string name = input.User.Trim().ToLowerInvariant();
        if (ValidateCredentials(input, register) is not null) return null;
        using var c = db.Open();
        if (register && input.Email is not null)
            return RegisterProfile(input);
        if (register)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            using var add = Database.Command(c, "INSERT OR IGNORE INTO accounts VALUES($n,$s,$h)",
                ("$n", name), ("$s", salt), ("$h", Password(input.Password, salt)));
            if (add.ExecuteNonQuery() != 1) return null;
        }
        else
        {
            using (var resolve = Database.Command(c, "SELECT name FROM account_profiles WHERE username=$n OR email=$n", ("$n", name)))
                name = resolve.ExecuteScalar() as string ?? name;
            using var get = Database.Command(c, "SELECT salt,hash FROM accounts WHERE name=$n", ("$n", name));
            using var rows = get.ExecuteReader();
            bool found = rows.Read();
            byte[] salt = found ? (byte[])rows[0] : new byte[16];
            byte[] hash = found ? (byte[])rows[1] : new byte[32];
            if (!CryptographicOperations.FixedTimeEquals(Password(input.Password, salt), hash) || !found) return null;
        }
        if (register) EnsureLegacyProfile(c, name);
        return IssueSession(c, name);
    }

    private AccountSession IssueSession(Microsoft.Data.Sqlite.SqliteConnection c, string name)
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var clean = Database.Command(c, "DELETE FROM tokens WHERE expires <= $t",
            ("$t", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        clean.ExecuteNonQuery();
        using var issue = Database.Command(c, "INSERT INTO tokens VALUES($h,$n,$e)", ("$h", TokenHash(token)),
            ("$n", name), ("$e", DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds()));
        issue.ExecuteNonQuery();
        return new(token, name, Settings(name).Username);
    }
    public string? Authenticate(string token)
    {
        if (token.Length != 64) return null;
        using var c = db.Open();
        using var cmd = Database.Command(c, "SELECT name FROM tokens WHERE hash=$h AND expires>$t",
            ("$h", TokenHash(token)), ("$t", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        return cmd.ExecuteScalar() as string;
    }
    public void Logout(string token)
    {
        lock (Gate) guests.Remove(GuestPrefix + TokenHash(token));
        using var c = db.Open();
        using var cmd = Database.Command(c, "DELETE FROM tokens WHERE hash=$h", ("$h", TokenHash(token)));
        cmd.ExecuteNonQuery();
    }
}
