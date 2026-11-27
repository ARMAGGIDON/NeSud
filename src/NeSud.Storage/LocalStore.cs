using NeSud.Core;

namespace NeSud.Storage;

public sealed class LocalStore
{
    private readonly Database database;
    private readonly string legacyPath;
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeSud", "client.db");
    public string? Warning { get;
    private set; }

    public LocalStore(string path, string legacyPath)
    {
        database = new Database(path);
        this.legacyPath = legacyPath;
        using var db = database.Open();
        using var cmd = Database.Command(db, """
            CREATE TABLE IF NOT EXISTS settings (id INTEGER PRIMARY KEY CHECK(id=1), payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS history (
              scope TEXT NOT NULL, id TEXT NOT NULL, payload TEXT NOT NULL, pending INTEGER NOT NULL,
              PRIMARY KEY(scope,id));
            CREATE INDEX IF NOT EXISTS ix_local_pending ON history(scope,id) WHERE pending=1;
            """);
        cmd.ExecuteNonQuery();
    }

    public AppState Load()
    {
        using var db = database.Open();
        using var cmd = Database.Command(db, "SELECT payload FROM settings WHERE id=1");
        if (cmd.ExecuteScalar() is string value) return Database.Decode<AppState > (value);
        var old = new StateStore(legacyPath);
        var state = old.Load();
        Warning = old.Warning;
        Save(state);
        return state;
    }

    public void Save(AppState state, GameRecord? terminal = null, string scope = "") =>
        SaveSnapshot(Database.Encode(state), terminal, scope);

    public void SaveSnapshot(string snapshot, GameRecord? terminal = null, string scope = "")
    {
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        using var cmd = Database.Command(db, "INSERT INTO settings VALUES(1,$p) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload",
            ("$p", snapshot));
        cmd.Transaction = tx;
        cmd.ExecuteNonQuery();
        if (terminal is not null)
        {
            using var h = Database.Command(db, "INSERT OR IGNORE INTO history VALUES($s,$id,$p,1)",
                ("$s", scope), ("$id", terminal.Id.ToString()), ("$p", Database.Encode(terminal)));
            h.Transaction = tx;
            h.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public List<GameRecord> History(string scope, bool pending = false)
    {
        using var db = database.Open();
        using var cmd = Database.Command(db, pending ? "SELECT payload FROM history WHERE scope=$s AND pending=1" : "SELECT payload FROM history WHERE scope=$s",
            ("$s", scope));
        using var rows = cmd.ExecuteReader();
        var result = new List<GameRecord > ();
        while (rows.Read()) result.Add(Database.Decode<GameRecord > (rows.GetString(0)));
        return result.OrderByDescending(r => r.Ended).ToList();
    }

    public void Merge(string scope, IEnumerable<GameRecord> records)
    {
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        foreach (var record in records)
        {
            using var cmd = Database.Command(db, """
                INSERT INTO history VALUES($s,$id,$p,0)
                ON CONFLICT(scope,id) DO UPDATE SET payload=excluded.payload,pending=0
                """, ("$s", scope), ("$id", record.Id.ToString()), ("$p", Database.Encode(record)));
            cmd.Transaction = tx;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void ClaimGuest(string scope)
    {
        if (scope.Length == 0) return;
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        using var copy = Database.Command(db, """
            INSERT OR IGNORE INTO history SELECT $s,id,payload,1 FROM history WHERE scope=''
            """, ("$s", scope));
        copy.Transaction = tx;
        copy.ExecuteNonQuery();
        using var delete = Database.Command(db, "DELETE FROM history WHERE scope=''");
        delete.Transaction = tx;
        delete.ExecuteNonQuery();
        tx.Commit();
    }
}
