using System.Security.Cryptography;
using System.Text;
using NeSud.Core;
using NeSud.Storage;

namespace NeSud.Server;

public sealed partial class ServerStore
{
    public WebGame? Session(string name, string? challengeDay = null)
    {
        if (challengeDay is not null) return ChallengeSession(name, challengeDay);
        if (IsGuest(name))
        {
            lock (Gate) return guests.TryGetValue(name, out var entry) ? entry.Game : null;
        }
        using var c = db.Open();
        using var cmd = Database.Command(c, "SELECT payload FROM web_sessions WHERE name=$n", ("$n", name));
        return cmd.ExecuteScalar() is string s ? Database.Decode<WebGame > (s) : null;
    }
    public void SaveSession(string name, WebGame session, GameRecord? terminal = null, string? walletKind = null)
    {
        if (session.ChallengeDay is not null)
        {
            SaveChallengeSession(name, session, terminal, walletKind);
            return;
        }
        if (IsGuest(name))
        {
            lock (Gate)
            {
                if (guests.ContainsKey(name)) guests[name] = (DateTimeOffset.UtcNow.AddHours(2), session);
            }
            return;
        }
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        if (walletKind is not null && !Debit(c, tx, name, walletKind)) throw new InvalidOperationException("Недостатньо ресурсів.");
        if (terminal is not null && Insert(c, tx, name, terminal) && terminal.Outcome == "completed")
        {
            var audit = session.Audit ?? new MoveAudit();
            using var log = Tx(c, tx, "INSERT OR IGNORE INTO speed_audit VALUES($n,$id,$m,$f,$flag)",
                ("$n", name), ("$id", terminal.Id.ToString()), ("$m", audit.Moves), ("$f", audit.FastIntervals), ("$flag", audit.Flag(terminal.Game) ? 1 : 0));
            log.ExecuteNonQuery();
            EnsureWinPacks(c, tx, name, Day(terminal.Ended));
        }
        using var cmd = Database.Command(c, """
            INSERT INTO web_sessions VALUES($n,$p) ON CONFLICT(name) DO UPDATE SET payload=excluded.payload
            """, ("$n", name), ("$p", Database.Encode(session)));
        cmd.Transaction = tx;
        cmd.ExecuteNonQuery();
        tx.Commit();
    }
}
