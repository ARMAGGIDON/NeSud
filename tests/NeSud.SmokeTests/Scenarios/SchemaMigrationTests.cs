using Microsoft.Data.Sqlite;
using NeSud.Core;
using NeSud.Server;
using NeSud.Storage;

namespace NeSud.SmokeTests;

internal static partial class SmokeTests
{
    private static void TestSchemaMigration(Puzzle puzzle)
    {
        string directory = Path.Combine(Path.GetTempPath(), "NeSud-schema-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "server.db");
        try
        {
            var database = new Database(path);
            var game = Game.Start(puzzle);
            var record = new GameRecord(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow, "desktop", "abandoned", game);
            using (var c = database.Open())
            {
                using var schema = Database.Command(c, """
                    CREATE TABLE accounts(name TEXT PRIMARY KEY,salt BLOB NOT NULL,hash BLOB NOT NULL);
                    INSERT INTO accounts VALUES('legacy-user',zeroblob(16),zeroblob(32));
                    CREATE TABLE history(name TEXT NOT NULL REFERENCES accounts(name),id TEXT NOT NULL,
                        source TEXT NOT NULL,score INTEGER NOT NULL,outcome TEXT NOT NULL,payload TEXT NOT NULL,
                        PRIMARY KEY(name,id));
                    CREATE TABLE daily_packs(name TEXT NOT NULL REFERENCES accounts(name),day TEXT NOT NULL,
                        milestone TEXT NOT NULL,slot INTEGER NOT NULL,options TEXT NOT NULL,chosen INTEGER,
                        PRIMARY KEY(name,day,milestone,slot));
                    INSERT INTO daily_packs VALUES('legacy-user','2026-01-01','win1',0,
                        '[{"kind":"life","amount":1},{"kind":"hint","amount":2},{"kind":"candidates","amount":3}]',1);
                    """);
                schema.ExecuteNonQuery();
                using var add = Database.Command(c, """
                    INSERT INTO history(rowid,name,id,source,score,outcome,payload)
                    VALUES(17,'legacy-user',$id,'desktop',0,'abandoned',$p)
                    """, ("$id", record.Id.ToString()), ("$p", Database.Encode(record)));
                add.ExecuteNonQuery();
            }

            var store = new ServerStore(path);
            var result = store.HistorySince("legacy-user", 16);
            Check(result.Cursor == 17 && result.Records.Single().Id == record.Id, "Preserve legacy sync cursor");
            Check(result.Records.Single().Game.Puzzle.Givens.SequenceEqual(puzzle.Givens) &&
                result.Records.Single().Game.Puzzle.Solution.SequenceEqual(puzzle.Solution) &&
                result.Records.Single().Ended == record.Ended, "Reconstruct migrated record");
            Check(store.HistorySince("legacy-user", 17).Records.Count == 0, "No replay of acknowledged record");
            var second = record with { Id = Guid.NewGuid() };
            store.Upload("legacy-user", [second, second]);
            Check(store.HistorySince("legacy-user", 17).Records.Single().Id == second.Id, "New cursor and idempotent upload");

            using (var c = database.Open())
            {
                long Scalar(string sql)
                {
                    using var cmd = Database.Command(c, sql);
                    return Convert.ToInt64(cmd.ExecuteScalar());
                }
                Check(Scalar("PRAGMA user_version") == 1, "Schema version");
                Check(Scalar("SELECT COUNT(*) FROM history_puzzles") == 1, "Deduplicated puzzle");
                Check(Scalar("SELECT COUNT(*) FROM history WHERE json_type(game,'$.puzzle') IS NOT NULL") == 0,
                    "No duplicate puzzle in history snapshot");
                Check(Scalar("SELECT COUNT(*) FROM daily_pack_cards") == 3 &&
                    Scalar("SELECT COUNT(*) FROM daily_pack_cards WHERE kind='hint'") == 2 &&
                    Scalar("SELECT chosen FROM daily_packs") == 1, "Preserve reward options and choice");
                Check(Scalar("SELECT COUNT(*) FROM pragma_foreign_key_check") == 0, "Foreign keys after migration");
                using var bad = Database.Command(c, "INSERT INTO speed_audit VALUES('legacy-user','missing',1,0,0)");
                bool rejected = false;
                try { bad.ExecuteNonQuery(); }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { rejected = true; }
                Check(rejected, "Audit must reference a game");
            }
            Check(new ServerStore(path).History("legacy-user").Count == 2, "Migration restart is idempotent");
            Console.WriteLine("PASS SQLite schema migration, cursor, puzzle deduplication, reward cards and FK");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
