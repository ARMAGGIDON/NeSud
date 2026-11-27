using System.Security.Cryptography;
using System.Text;
using NeSud.Core;
using NeSud.Storage;

namespace NeSud.Server;

public sealed partial class ServerStore
{
    private readonly Database db;
    public object Gate { get; } = new();
    public ServerStore(string path)
    {
        db = new Database(path);
        using var c = db.Open();
        using var cmd = Database.Command(c, """
            CREATE TABLE IF NOT EXISTS accounts (name TEXT PRIMARY KEY, salt BLOB NOT NULL, hash BLOB NOT NULL);
            CREATE TABLE IF NOT EXISTS account_profiles (
                name TEXT PRIMARY KEY REFERENCES accounts(name), username TEXT NOT NULL COLLATE NOCASE UNIQUE,
                email TEXT COLLATE NOCASE UNIQUE, verified INTEGER NOT NULL DEFAULT 0,
                invite TEXT NOT NULL UNIQUE, referred_by TEXT REFERENCES accounts(name),
                avatar BLOB, verification_hash TEXT, verification_expiry INTEGER NOT NULL DEFAULT 0,
                verification_sent INTEGER NOT NULL DEFAULT 0);
            CREATE TRIGGER IF NOT EXISTS immutable_invite
                BEFORE UPDATE OF invite ON account_profiles
                WHEN OLD.invite IS NOT NEW.invite
                BEGIN SELECT RAISE(ABORT,'Invitation code is immutable'); END;
            CREATE TABLE IF NOT EXISTS referral_rewards (
                invited TEXT PRIMARY KEY REFERENCES accounts(name), inviter TEXT NOT NULL REFERENCES accounts(name),
                rewarded_at INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS tokens (hash TEXT PRIMARY KEY, name TEXT NOT NULL REFERENCES accounts(name), expires INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS history (
              name TEXT NOT NULL REFERENCES accounts(name), id TEXT NOT NULL, source TEXT NOT NULL,
              score INTEGER NOT NULL, outcome TEXT NOT NULL, payload TEXT NOT NULL, PRIMARY KEY(name,id));
            CREATE TABLE IF NOT EXISTS web_sessions (name TEXT PRIMARY KEY REFERENCES accounts(name), payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS speed_audit (name TEXT NOT NULL, id TEXT NOT NULL, moves INTEGER NOT NULL, fast_intervals INTEGER NOT NULL, flagged INTEGER NOT NULL, PRIMARY KEY(name,id));
            CREATE TABLE IF NOT EXISTS daily_packs (name TEXT NOT NULL REFERENCES accounts(name), day TEXT NOT NULL, milestone TEXT NOT NULL, slot INTEGER NOT NULL, options TEXT NOT NULL, chosen INTEGER, PRIMARY KEY(name,day,milestone,slot));
            CREATE TABLE IF NOT EXISTS wallet (name TEXT NOT NULL REFERENCES accounts(name), kind TEXT NOT NULL, amount INTEGER NOT NULL CHECK(amount>=0), PRIMARY KEY(name,kind));
            CREATE TABLE IF NOT EXISTS wallet_spends (name TEXT NOT NULL REFERENCES accounts(name), operation TEXT NOT NULL, game_id TEXT NOT NULL, kind TEXT NOT NULL, PRIMARY KEY(name,operation));





            CREATE INDEX IF NOT EXISTS ix_daily_pending ON daily_packs(name,day) WHERE chosen IS NULL;
            CREATE INDEX IF NOT EXISTS ix_tokens_expiry ON tokens(expires);
            CREATE TABLE IF NOT EXISTS tournament_state (id INTEGER PRIMARY KEY CHECK(id=1), next_week TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS tournament_results (week TEXT NOT NULL, rank INTEGER NOT NULL, name TEXT NOT NULL REFERENCES accounts(name), wins INTEGER NOT NULL, score INTEGER NOT NULL, PRIMARY KEY(week,rank), UNIQUE(week,name));
            CREATE TABLE IF NOT EXISTS friendships (sender TEXT NOT NULL REFERENCES accounts(name), recipient TEXT NOT NULL REFERENCES accounts(name), accepted INTEGER NOT NULL DEFAULT 0 CHECK(accepted IN (0,1)), CHECK(sender<>recipient), PRIMARY KEY(sender,recipient));
            CREATE INDEX IF NOT EXISTS ix_friend_recipient ON friendships(recipient,accepted);

            CREATE TABLE IF NOT EXISTS challenge_puzzles (day TEXT PRIMARY KEY, payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS challenge_sessions (name TEXT NOT NULL REFERENCES accounts(name), day TEXT NOT NULL REFERENCES challenge_puzzles(day), payload TEXT NOT NULL, PRIMARY KEY(name,day));
            CREATE INDEX IF NOT EXISTS ix_challenge_sessions_day ON challenge_sessions(day);
            CREATE TABLE IF NOT EXISTS challenge_difficulties (day TEXT PRIMARY KEY, difficulty INTEGER NOT NULL CHECK(difficulty BETWEEN 1 AND 3));
            CREATE TABLE IF NOT EXISTS challenge_wins (name TEXT NOT NULL REFERENCES accounts(name), day TEXT NOT NULL REFERENCES challenge_puzzles(day), PRIMARY KEY(name,day));
            CREATE TABLE IF NOT EXISTS monthly_cups (name TEXT NOT NULL REFERENCES accounts(name), month TEXT NOT NULL, PRIMARY KEY(name,month));
            CREATE TABLE IF NOT EXISTS desktop_drafts (name TEXT NOT NULL REFERENCES accounts(name), id TEXT NOT NULL, updated TEXT NOT NULL, payload TEXT NOT NULL, PRIMARY KEY(name,id));
            """);
        cmd.ExecuteNonQuery();
        using var migration = db.Open();
        using var tx = migration.BeginTransaction();
        using var convert = Tx(migration, tx, """
            INSERT INTO wallet(name,kind,amount) SELECT name,'hint',amount FROM wallet WHERE kind='life'
            ON CONFLICT(name,kind) DO UPDATE SET amount=amount+excluded.amount;
            DELETE FROM wallet WHERE kind='life';
            """);
        convert.ExecuteNonQuery();
        MigrateProfiles(migration, tx);
        NormalizeSchema(migration, tx);
        MigrateChallengeHistory(migration, tx);
        using var seed = Tx(migration, tx, "INSERT OR IGNORE INTO tournament_state VALUES(1,$w)", ("$w", Week(DateTimeOffset.UtcNow)));
        seed.ExecuteNonQuery();
        PruneChallengeSessions(migration, tx);
        tx.Commit();
    }
}
