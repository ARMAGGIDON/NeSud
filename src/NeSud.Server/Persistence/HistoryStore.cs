using System.Security.Cryptography;
using System.Text;
using NeSud.Core;
using NeSud.Storage;

namespace NeSud.Server;

public sealed partial class ServerStore
{
    public List<GameRecord> History(string name)
    {
        using var c = db.Open();
        using var cmd = Database.Command(c,
            "SELECT payload FROM history WHERE name=$n", ("$n", name));
        using var rows = cmd.ExecuteReader();
        var result = new List<GameRecord > ();
        while (rows.Read()) result.Add(Database.Decode<GameRecord > (rows.GetString(0)));
        return result.OrderByDescending(r => r.Ended).ToList();
    }
    public HistoryPage HistorySince(string name, long cursor)
    {
        using var c = db.Open();
        using var cmd = Database.Command(c, "SELECT rowid,payload FROM history WHERE name=$n AND rowid>$r ORDER BY rowid LIMIT 100", ("$n", name), ("$r", cursor));
        using var rows = cmd.ExecuteReader();
        var records = new List<GameRecord > ();
        while (rows.Read())
        {
            cursor = rows.GetInt64(0);
            records.Add(Database.Decode<GameRecord > (rows.GetString(1)));
        }
        return new(cursor, records);
    }
    public List<GameRecord> Acknowledge(string name, IEnumerable<GameRecord> records)
    {
        using var c = db.Open();
        var result = new List<GameRecord > ();
        foreach (var r in records)
        {
            using var cmd = Database.Command(c, "SELECT payload FROM history WHERE name=$n AND id=$id", ("$n", name), ("$id", r.Id.ToString()));
            if (cmd.ExecuteScalar() is string value) result.Add(Database.Decode<GameRecord > (value));
        }
        return result;
    }
    private static bool Insert(Microsoft.Data.Sqlite.SqliteConnection c, Microsoft.Data.Sqlite.SqliteTransaction tx,
        string name, GameRecord record)
    {
        using var cmd = Database.Command(c, "INSERT OR IGNORE INTO history VALUES($n,$id,$src,$score,$o,$p)",
            ("$n", name), ("$id", record.Id.ToString()), ("$src", record.Source),
            ("$score", record.Score), ("$o", record.Outcome), ("$p", Database.Encode(record)));
        cmd.Transaction = tx;
        return cmd.ExecuteNonQuery() == 1;
    }
    public void Upload(string name, List<GameRecord> records)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        var days = new HashSet<string > ();
        foreach (var r in records)
        {
            if (Insert(c, tx, name, r) && r.Outcome == "completed") days.Add(Day(r.Ended));
            using var del = Database.Command(c, "DELETE FROM desktop_drafts WHERE name=$n AND id=$id", ("$n", name), ("$id", r.Id.ToString()));
            del.Transaction = tx;
            del.ExecuteNonQuery();
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
        using var rows = cmd.ExecuteReader();
        var result = new List<DesktopDraft > ();
        while (rows.Read()) result.Add(Database.Decode<DesktopDraft > (rows.GetString(0)));
        return result;
    }
}
