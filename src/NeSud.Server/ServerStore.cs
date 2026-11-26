using System.Security.Cryptography;
using System.Text;
using NeSud.Core;
using NeSud.Storage;

namespace NeSud.Server;

public sealed record WebGame(Guid Id, DateTimeOffset Started, Game Game, DateTimeOffset LastSeen, long Revision);
public sealed class ServerStore
{
    private readonly Database db;
    public object Gate { get; } = new();
    public ServerStore(string path)
    {
        db = new Database(path);
        using var c = db.Open();
        using var cmd = Database.Command(c, """
            CREATE TABLE IF NOT EXISTS accounts (name TEXT PRIMARY KEY, salt BLOB NOT NULL, hash BLOB NOT NULL);
            CREATE TABLE IF NOT EXISTS tokens (hash TEXT PRIMARY KEY, name TEXT NOT NULL REFERENCES accounts(name), expires INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS history (
              name TEXT NOT NULL REFERENCES accounts(name), id TEXT NOT NULL, source TEXT NOT NULL,
              score INTEGER NOT NULL, outcome TEXT NOT NULL, payload TEXT NOT NULL, PRIMARY KEY(name,id));
            CREATE TABLE IF NOT EXISTS web_sessions (name TEXT PRIMARY KEY REFERENCES accounts(name), payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS desktop_drafts (name TEXT NOT NULL REFERENCES accounts(name), id TEXT NOT NULL, updated TEXT NOT NULL, payload TEXT NOT NULL, PRIMARY KEY(name,id));
            """);
        cmd.ExecuteNonQuery();
    }
    private static byte[] Password(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000, HashAlgorithmName.SHA256, 32);
    private static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public AccountSession? Login(Credentials input, bool register)
    {
        string name = input.User.Trim().ToLowerInvariant();
        if (name.Length is < 3 or > 32 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ||
            input.Password.Length is < 10 or > 128) return null;
        using var c = db.Open();
        if (register)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            using var add = Database.Command(c, "INSERT OR IGNORE INTO accounts VALUES($n,$s,$h)",
                ("$n", name), ("$s", salt), ("$h", Password(input.Password, salt)));
            if (add.ExecuteNonQuery() != 1) return null;
        }
        else
        {
            using var get = Database.Command(c, "SELECT salt,hash FROM accounts WHERE name=$n", ("$n", name));
            using var rows = get.ExecuteReader();
            bool found = rows.Read();
            byte[] salt = found ? (byte[])rows[0] : new byte[16];
            byte[] hash = found ? (byte[])rows[1] : new byte[32];
            if (!CryptographicOperations.FixedTimeEquals(Password(input.Password, salt), hash) || !found) return null;
        }
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var clean = Database.Command(c, "DELETE FROM tokens WHERE expires <= $t",
            ("$t", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        clean.ExecuteNonQuery();
        using var issue = Database.Command(c, "INSERT INTO tokens VALUES($h,$n,$e)", ("$h", TokenHash(token)),
            ("$n", name), ("$e", DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds()));
        issue.ExecuteNonQuery(); return new(token, name);
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
        using var c = db.Open();
        using var cmd = Database.Command(c, "DELETE FROM tokens WHERE hash=$h", ("$h", TokenHash(token)));
        cmd.ExecuteNonQuery();
    }
    public List<GameRecord> History(string name)
    {
        using var c = db.Open(); using var cmd = Database.Command(c,
            "SELECT payload FROM history WHERE name=$n", ("$n", name));
        using var rows = cmd.ExecuteReader(); var result = new List<GameRecord>();
        while (rows.Read()) result.Add(Database.Decode<GameRecord>(rows.GetString(0)));
        return result.OrderByDescending(r => r.Ended).ToList();
    }
    private static void Insert(Microsoft.Data.Sqlite.SqliteConnection c, Microsoft.Data.Sqlite.SqliteTransaction tx,
        string name, GameRecord record)
    {
        using var cmd = Database.Command(c, "INSERT OR IGNORE INTO history VALUES($n,$id,$src,$score,$o,$p)",
            ("$n", name), ("$id", record.Id.ToString()), ("$src", record.Source),
            ("$score", record.Score), ("$o", record.Outcome), ("$p", Database.Encode(record)));
        cmd.Transaction = tx; cmd.ExecuteNonQuery();
    }
    public void Upload(string name, List<GameRecord> records)
    {
        using var c = db.Open(); using var tx = c.BeginTransaction();
        foreach (var r in records)
        {
            Insert(c, tx, name, r);
            using var del = Database.Command(c, "DELETE FROM desktop_drafts WHERE name=$n AND id=$id", ("$n", name), ("$id", r.Id.ToString()));
            del.Transaction = tx; del.ExecuteNonQuery();
        }
        tx.Commit();
    }
    public bool SaveDraft(string name, DesktopDraft draft)
    {
        using var c = db.Open();
        using var cmd = Database.Command(c, """
            INSERT INTO desktop_drafts(name,id,updated,payload)
            SELECT $n,$id,$u,$p WHERE NOT EXISTS(SELECT 1 FROM history WHERE name=$n AND id=$id)
            ON CONFLICT(name,id) DO UPDATE SET updated=excluded.updated,payload=excluded.payload
            WHERE excluded.updated >= desktop_drafts.updated
            """, ("$n", name), ("$id", draft.Id.ToString()), ("$u", draft.Updated.ToUniversalTime().ToString("O")),
            ("$p", Database.Encode(draft)));
        return cmd.ExecuteNonQuery() == 1;
    }
    public List<DesktopDraft> Drafts(string name)
    {
        using var c = db.Open();
        using var cmd = Database.Command(c, "SELECT payload FROM desktop_drafts WHERE name=$n ORDER BY updated DESC", ("$n", name));
        using var rows = cmd.ExecuteReader(); var result = new List<DesktopDraft>();
        while (rows.Read()) result.Add(Database.Decode<DesktopDraft>(rows.GetString(0)));
        return result;
    }
    public List<RankEntry> Rankings()
    {
        using var c = db.Open(); using var cmd = Database.Command(c, """
            SELECT name,COUNT(*),SUM(score) FROM history WHERE source='web' AND outcome='completed'
            GROUP BY name ORDER BY SUM(score) DESC,name LIMIT 100
            """);
        using var rows = cmd.ExecuteReader(); var result = new List<RankEntry>();
        while (rows.Read()) result.Add(new(rows.GetString(0), rows.GetInt32(1), rows.GetInt64(2)));
        return result;
    }
    public WebGame? Session(string name)
    {
        using var c = db.Open(); using var cmd = Database.Command(c, "SELECT payload FROM web_sessions WHERE name=$n", ("$n", name));
        return cmd.ExecuteScalar() is string s ? Database.Decode<WebGame>(s) : null;
    }
    public void SaveSession(string name, WebGame session, GameRecord? terminal = null)
    {
        using var c = db.Open(); using var tx = c.BeginTransaction();
        if (terminal is not null) Insert(c, tx, name, terminal);
        using var cmd = Database.Command(c, """
            INSERT INTO web_sessions VALUES($n,$p) ON CONFLICT(name) DO UPDATE SET payload=excluded.payload
            """, ("$n", name), ("$p", Database.Encode(session)));
        cmd.Transaction = tx; cmd.ExecuteNonQuery(); tx.Commit();
    }
}
