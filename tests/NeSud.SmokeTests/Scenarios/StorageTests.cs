using NeSud.Core;
using NeSud.Storage;
using NeSud.Server;
using System.Numerics;

namespace NeSud.SmokeTests;

internal static partial class SmokeTests
{
    private static void TestStorage(List<Puzzle> puzzles)
    {
    var dbDir = Path.Combine(Path.GetTempPath(), "NeSud-db-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var local = new LocalStore(Path.Combine(dbDir, "client.db"), Path.Combine(dbDir, "legacy.json"));
            using (var connection = new Database(Path.Combine(dbDir, "client.db")).Open())
            using (var versionQuery = Database.Command(connection, "SELECT sqlite_version()"))
            {
                var version = Version.Parse((string)versionQuery.ExecuteScalar()!);
                Check(version >= new Version(3, 50, 2), "Native SQLite must include the CVE-2025-6965 fix");
                Console.WriteLine($"PASS native SQLite {version}");
            }
            var attempt = Game.Start(puzzles[0]);
            var appState = new AppState { Game = attempt, GameId = Guid.NewGuid(), Started = DateTimeOffset.UtcNow };
            var record = new GameRecord(appState.GameId, appState.Started, DateTimeOffset.UtcNow, "desktop", "abandoned", attempt);
            local.Save(appState, record, "account-a");
            local.Save(appState, record, "account-a");
            Check(local.History("account-a", true).Count == 1, "Idempotent local history");
            Check(local.History("account-b").Count == 0, "Account isolation");
            local.Merge("account-a", [record]);
            Check(local.History("account-a", true).Count == 0 && local.History("account-a").Count == 1, "Sync acknowledgment");
            Check(local.Load().GameId == appState.GameId, "SQLite current game");
            Console.WriteLine("PASS local SQLite, account isolation, history and synchronization");
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(dbDir)) Directory.Delete(dbDir, true); }
    }
}
