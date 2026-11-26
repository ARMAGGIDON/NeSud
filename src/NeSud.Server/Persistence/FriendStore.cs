using NeSud.Core;
using NeSud.Storage;

namespace NeSud.Server;

public sealed partial class ServerStore
{
    public List<string> FindPlayers(string q)
    {
        q = q.Trim();
        if (q.Length < 2 || q.Length > 80 || q.Contains('@'))return [];
        using var c = db.Open();
        using var cmd = Database.Command(c, """
            SELECT label FROM (SELECT username label FROM account_profiles WHERE verified=1)
            WHERE instr(lower(label),lower($q))>0 ORDER BY label LIMIT 30
            """, ("$q", q));
        using var rows = cmd.ExecuteReader();
        var result = new List<string > ();
        while (rows.Read())result.Add(rows.GetString(0));
        return result;
    }
    public List<FriendEntry> Friends(string name)
    {
        using var c = db.Open();
        using var cmd = Database.Command(c, """
            SELECT a.username,CASE WHEN f.accepted=1 THEN 'friend' WHEN f.sender=$n THEN 'outgoing' ELSE 'incoming' END
            FROM friendships f JOIN account_profiles a ON a.name=CASE WHEN f.sender=$n THEN f.recipient ELSE f.sender END
            WHERE (f.sender=$n OR f.recipient=$n) AND a.verified=1
              AND EXISTS(SELECT 1 FROM account_profiles me WHERE me.name=$n AND me.verified=1)
            ORDER BY f.accepted DESC,a.name
            """, ("$n", name));
        using var rows = cmd.ExecuteReader();
        var result = new List<FriendEntry > ();
        while (rows.Read())result.Add(new(rows.GetString(0), rows.GetString(1)));
        return result;
    }
    public bool ChangeFriend(string name, FriendRequest request)
    {
        if (request.User is null || request.Action is null || ResolvePublicName(request.User) is not { } other || other == name)return false;
        lock (Gate)
        {
            using var c = db.Open();
            using var tx = c.BeginTransaction();
            using (var eligible = Tx(c, tx,
                "SELECT count(*) FROM account_profiles WHERE name IN ($n,$o) AND verified=1",
                ("$n", name), ("$o", other)))
                if (Convert.ToInt32(eligible.ExecuteScalar()) != 2) return false;

            string? sql = request.Action switch
            {
                "send" => """
                    INSERT INTO friendships(sender,recipient) SELECT $n,$o WHERE NOT EXISTS(
                        SELECT 1 FROM friendships WHERE (sender=$n AND recipient=$o) OR (sender=$o AND recipient=$n))
                    """,
                "accept" => "UPDATE friendships SET accepted=1 WHERE sender=$o AND recipient=$n AND accepted=0",
                "decline" => "DELETE FROM friendships WHERE sender=$o AND recipient=$n AND accepted=0",
                "cancel" => "DELETE FROM friendships WHERE sender=$n AND recipient=$o AND accepted=0",
                "remove" => "DELETE FROM friendships WHERE accepted=1 AND ((sender=$n AND recipient=$o) OR (sender=$o AND recipient=$n))",
                _ => null
            };
            if (sql is null)return false;
            using var cmd = Tx(c, tx, sql, ("$n", name), ("$o", other));
            bool changed = cmd.ExecuteNonQuery() == 1;
            tx.Commit();
            return changed;
        }
    }
}
