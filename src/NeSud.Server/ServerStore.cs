using System.Security.Cryptography;
using System.Text;
using NeSud.Core;
using NeSud.Storage;

namespace NeSud.Server;

public sealed record RewardTicket(string Kind, DateTimeOffset ReadyAt, DateTimeOffset ExpiresAt);
public sealed record MoveAudit(int Moves = 0, int FastIntervals = 0, DateTimeOffset? LastMove = null)
{
    public MoveAudit Record(DateTimeOffset now) => this with
    { Moves = Moves + 1, FastIntervals = FastIntervals + (LastMove is { } last && (now-last).TotalMilliseconds < 90 ? 1 : 0), LastMove = now };
    public bool Flag(Game game) => Moves >= 16 && (FastIntervals >= 12 && FastIntervals * 5 >= (Moves - 1) * 4 ||
        game.Seconds * 1000L < Moves * 90L);
}
public sealed record WebGame(Guid Id, DateTimeOffset Started, Game Game, DateTimeOffset LastSeen, long Revision, RewardTicket? Reward = null, MoveAudit? Audit = null, double Fraction = 0, string? ChallengeDay = null);
public sealed partial class ServerStore
{
    private readonly Database db;
    public object Gate { get; } = new();
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
            { guests[key] = (DateTimeOffset.UtcNow.AddHours(2), entry.Game); return key; }
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
        if (register && input.Password != input.Confirmation) return "Паролі не збігаються.";
        return null;
    }
    private static string PublicName(string name, byte[] salt) => name.Contains('@')
        ? "Гравець-" + Convert.ToHexString(salt)[..12]
        : name;
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
            CREATE TABLE IF NOT EXISTS speed_audit (name TEXT NOT NULL, id TEXT NOT NULL, moves INTEGER NOT NULL, fast_intervals INTEGER NOT NULL, flagged INTEGER NOT NULL, PRIMARY KEY(name,id));
            CREATE TABLE IF NOT EXISTS daily_packs (name TEXT NOT NULL REFERENCES accounts(name), day TEXT NOT NULL, milestone TEXT NOT NULL, slot INTEGER NOT NULL, options TEXT NOT NULL, chosen INTEGER, PRIMARY KEY(name,day,milestone,slot));
            CREATE TABLE IF NOT EXISTS wallet (name TEXT NOT NULL REFERENCES accounts(name), kind TEXT NOT NULL, amount INTEGER NOT NULL CHECK(amount>=0), PRIMARY KEY(name,kind));
            CREATE TABLE IF NOT EXISTS wallet_spends (name TEXT NOT NULL REFERENCES accounts(name), operation TEXT NOT NULL, game_id TEXT NOT NULL, kind TEXT NOT NULL, PRIMARY KEY(name,operation));
            CREATE INDEX IF NOT EXISTS ix_history_name ON history(name);
            CREATE INDEX IF NOT EXISTS ix_history_day ON history(name, outcome, julianday(json_extract(payload,'$.ended')));
            CREATE INDEX IF NOT EXISTS ix_history_rank ON history(source,outcome,name,score);
            CREATE INDEX IF NOT EXISTS ix_history_compare ON history(source,outcome,json_extract(payload,'$.game.puzzle.difficulty'),coalesce(json_extract(payload,'$.game.rulesVersion'),0));
            CREATE INDEX IF NOT EXISTS ix_history_week ON history(source,outcome,julianday(json_extract(payload,'$.ended')));
            CREATE INDEX IF NOT EXISTS ix_daily_pending ON daily_packs(name,day) WHERE chosen IS NULL;
            CREATE INDEX IF NOT EXISTS ix_tokens_expiry ON tokens(expires);
            CREATE TABLE IF NOT EXISTS tournament_state (id INTEGER PRIMARY KEY CHECK(id=1), next_week TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS tournament_results (week TEXT NOT NULL, rank INTEGER NOT NULL, name TEXT NOT NULL REFERENCES accounts(name), wins INTEGER NOT NULL, score INTEGER NOT NULL, PRIMARY KEY(week,rank), UNIQUE(week,name));
            CREATE TABLE IF NOT EXISTS friendships (sender TEXT NOT NULL REFERENCES accounts(name), recipient TEXT NOT NULL REFERENCES accounts(name), accepted INTEGER NOT NULL DEFAULT 0 CHECK(accepted IN (0,1)), CHECK(sender<>recipient), PRIMARY KEY(sender,recipient));
            CREATE INDEX IF NOT EXISTS ix_friend_recipient ON friendships(recipient,accepted);
            CREATE INDEX IF NOT EXISTS ix_history_level ON history(name,json_extract(payload,'$.game.puzzle.difficulty'));
            CREATE TABLE IF NOT EXISTS challenge_puzzles (day TEXT PRIMARY KEY, payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS challenge_sessions (name TEXT NOT NULL REFERENCES accounts(name), day TEXT NOT NULL REFERENCES challenge_puzzles(day), payload TEXT NOT NULL, PRIMARY KEY(name,day));
            CREATE INDEX IF NOT EXISTS ix_challenge_sessions_day ON challenge_sessions(day);
            CREATE TABLE IF NOT EXISTS challenge_attempts (name TEXT NOT NULL REFERENCES accounts(name), id TEXT NOT NULL, day TEXT NOT NULL REFERENCES challenge_puzzles(day), outcome TEXT NOT NULL, ended TEXT NOT NULL, payload TEXT NOT NULL, PRIMARY KEY(name,id));
            CREATE INDEX IF NOT EXISTS ix_challenge_attempts_history ON challenge_attempts(name,ended DESC);
            CREATE TABLE IF NOT EXISTS challenge_wins (name TEXT NOT NULL REFERENCES accounts(name), day TEXT NOT NULL REFERENCES challenge_puzzles(day), PRIMARY KEY(name,day));
            CREATE TABLE IF NOT EXISTS monthly_cups (name TEXT NOT NULL REFERENCES accounts(name), month TEXT NOT NULL, PRIMARY KEY(name,month));
            CREATE TABLE IF NOT EXISTS desktop_drafts (name TEXT NOT NULL REFERENCES accounts(name), id TEXT NOT NULL, updated TEXT NOT NULL, payload TEXT NOT NULL, PRIMARY KEY(name,id));
            """);
        cmd.ExecuteNonQuery();
        using var migration = db.Open(); using var tx = migration.BeginTransaction();
        using var convert = Tx(migration, tx, """
            INSERT INTO wallet(name,kind,amount) SELECT name,'hint',amount FROM wallet WHERE kind='life'
            ON CONFLICT(name,kind) DO UPDATE SET amount=amount+excluded.amount;
            DELETE FROM wallet WHERE kind='life';
            UPDATE daily_packs SET options=replace(options,'"kind":"life"','"kind":"hint"') WHERE options LIKE '%"kind":"life"%';
            """);
        convert.ExecuteNonQuery();
        using var seed = Tx(migration, tx, "INSERT OR IGNORE INTO tournament_state VALUES(1,$w)", ("$w", Week(DateTimeOffset.UtcNow)));
        seed.ExecuteNonQuery(); PruneChallengeSessions(migration,tx); tx.Commit();
    }
    private static byte[] Password(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000, HashAlgorithmName.SHA256, 32);
    private static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public AccountSession? Login(Credentials input, bool register)
    {
        string name = input.User.Trim().ToLowerInvariant();
        if (ValidateCredentials(input, register) is not null) return null;
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
        lock (Gate) guests.Remove(GuestPrefix + TokenHash(token));
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
    public HistoryPage HistorySince(string name, long cursor)
    {
        using var c = db.Open(); using var cmd = Database.Command(c, "SELECT rowid,payload FROM history WHERE name=$n AND rowid>$r ORDER BY rowid LIMIT 100", ("$n", name), ("$r", cursor));
        using var rows = cmd.ExecuteReader(); var records = new List<GameRecord>();
        while (rows.Read()) { cursor = rows.GetInt64(0); records.Add(Database.Decode<GameRecord>(rows.GetString(1))); }
        return new(cursor, records);
    }
    public List<GameRecord> Acknowledge(string name, IEnumerable<GameRecord> records)
    {
        using var c = db.Open(); var result = new List<GameRecord>();
        foreach (var r in records) { using var cmd = Database.Command(c, "SELECT payload FROM history WHERE name=$n AND id=$id", ("$n", name), ("$id", r.Id.ToString())); if (cmd.ExecuteScalar() is string value) result.Add(Database.Decode<GameRecord>(value)); }
        return result;
    }
    private static bool Insert(Microsoft.Data.Sqlite.SqliteConnection c, Microsoft.Data.Sqlite.SqliteTransaction tx,
        string name, GameRecord record)
    {
        using var cmd = Database.Command(c, "INSERT OR IGNORE INTO history VALUES($n,$id,$src,$score,$o,$p)",
            ("$n", name), ("$id", record.Id.ToString()), ("$src", record.Source),
            ("$score", record.Score), ("$o", record.Outcome), ("$p", Database.Encode(record)));
        cmd.Transaction = tx; return cmd.ExecuteNonQuery() == 1;
    }
    public void Upload(string name, List<GameRecord> records)
    {
        using var c = db.Open(); using var tx = c.BeginTransaction();
        var days = new HashSet<string>();
        foreach (var r in records)
        {
            if (Insert(c, tx, name, r) && r.Outcome == "completed") days.Add(Day(r.Ended));
            using var del = Database.Command(c, "DELETE FROM desktop_drafts WHERE name=$n AND id=$id", ("$n", name), ("$id", r.Id.ToString()));
            del.Transaction = tx; del.ExecuteNonQuery();
        }
        foreach (var day in days) EnsureWinPacks(c, tx, name, day);
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
    public List<RankEntry> Rankings(bool all = false)
    {
        using var c = db.Open(); using var cmd = Database.Command(c, """
            SELECT h.name,COUNT(*),SUM(h.score),a.salt FROM history h JOIN accounts a ON a.name=h.name
            WHERE ($all=1 OR h.source='web') AND h.outcome='completed'
            AND NOT EXISTS(SELECT 1 FROM speed_audit v WHERE v.name=h.name AND v.id=h.id AND v.flagged=1)
            GROUP BY h.name,a.salt ORDER BY SUM(h.score) DESC,h.name LIMIT 100
            """, ("$all", all ? 1 : 0));
        using var rows = cmd.ExecuteReader(); var result = new List<RankEntry>();
        while (rows.Read()) result.Add(new(PublicName(rows.GetString(0), (byte[])rows[3]), rows.GetInt32(1), rows.GetInt64(2)));
        return result;
    }
    public DifficultyComparison Compare(Difficulty level, int score, int seconds, int rules = 3)
    {
        using var c = db.Open(); using var cmd = Database.Command(c, """
            SELECT h.name,h.score,json_extract(h.payload,'$.game.seconds'),json_extract(h.payload,'$.game.mistakes'),json_extract(h.payload,'$.game.hints'),coalesce(json_extract(h.payload,'$.game.candidateChanges'),0) FROM history h WHERE h.source='web' AND h.outcome='completed'
            AND json_extract(h.payload,'$.game.puzzle.difficulty')=$d
            AND coalesce(json_extract(h.payload,'$.game.rulesVersion'),0)=$r
            AND NOT EXISTS(SELECT 1 FROM speed_audit a WHERE a.name=h.name AND a.id=h.id AND a.flagged=1)
            """, ("$d", (int)level), ("$r", rules));
        using var rows = cmd.ExecuteReader();
        var records = new List<(int Score,int Seconds,int Mistakes,int Hints,long CandidateChanges)>(); var users = new HashSet<string>();
        while (rows.Read()) { users.Add(rows.GetString(0)); records.Add((rows.GetInt32(1),rows.GetInt32(2),rows.GetInt32(3),rows.GetInt32(4),rows.GetInt64(5))); }
        if (records.Count == 0) return new(0,0,0,0,0,0,0,0,0,0,0);
        var times = records.Select(r => r.Seconds).Order().ToArray(); int n = times.Length;
        double median = ((long)times[(n-1)/2]+times[n/2])/2.0;
        return new(n, users.Count, times.Average(), median, times[0], records.Average(r => r.Score),
            records.Average(r => r.Mistakes), records.Average(r => r.Hints), records.Average(r => r.CandidateChanges),
            (int)Math.Round(100.0 * records.Count(r => r.Score <= score)/n),
            (int)Math.Round(100.0 * times.Count(t => t >= seconds)/n));
    }
    public WebGame? Session(string name, string? challengeDay = null)
    {
        if (challengeDay is not null) return ChallengeSession(name, challengeDay);
        if (IsGuest(name)) { lock (Gate) return guests.TryGetValue(name, out var entry) ? entry.Game : null; }
        using var c = db.Open(); using var cmd = Database.Command(c, "SELECT payload FROM web_sessions WHERE name=$n", ("$n", name));
        return cmd.ExecuteScalar() is string s ? Database.Decode<WebGame>(s) : null;
    }
    public void SaveSession(string name, WebGame session, GameRecord? terminal = null, string? walletKind = null)
    {
        if (session.ChallengeDay is not null) { SaveChallengeSession(name, session, terminal, walletKind); return; }
        if (IsGuest(name)) { lock (Gate) { if (guests.ContainsKey(name)) guests[name] = (DateTimeOffset.UtcNow.AddHours(2), session); } return; }
        using var c = db.Open(); using var tx = c.BeginTransaction();
        if (walletKind is not null && !Debit(c, tx, name, walletKind)) throw new InvalidOperationException("Недостатньо ресурсів.");
        if (terminal is not null && Insert(c, tx, name, terminal) && terminal.Outcome == "completed")
        {
            var audit = session.Audit ?? new MoveAudit();
            using var log = Tx(c, tx, "INSERT OR IGNORE INTO speed_audit VALUES($n,$id,$m,$f,$flag)",
                ("$n", name), ("$id", terminal.Id.ToString()), ("$m", audit.Moves), ("$f", audit.FastIntervals), ("$flag", audit.Flag(terminal.Game) ? 1 : 0));
            log.ExecuteNonQuery(); EnsureWinPacks(c, tx, name, Day(terminal.Ended));
        }
        using var cmd = Database.Command(c, """
            INSERT INTO web_sessions VALUES($n,$p) ON CONFLICT(name) DO UPDATE SET payload=excluded.payload
            """, ("$n", name), ("$p", Database.Encode(session)));
        cmd.Transaction = tx; cmd.ExecuteNonQuery(); tx.Commit();
    }
}
