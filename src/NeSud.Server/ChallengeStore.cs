using Microsoft.Data.Sqlite;
using NeSud.Core;
using NeSud.Storage;
using System.Globalization;

namespace NeSud.Server;

public sealed partial class ServerStore
{
    private static bool ParseDate(string value, out DateOnly day) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
    private static string MonthKey(DateOnly day) => day.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    public bool ChallengeAllowed(string value) => ParseDate(value,out var date) &&
        MonthKey(date)==Day(DateTimeOffset.UtcNow)[..7] && string.CompareOrdinal(value,Day(DateTimeOffset.UtcNow))<=0;

    public Puzzle ChallengePuzzle(string day, CancellationToken token = default)
    {
        using (var c=db.Open())
        using (var cmd=Database.Command(c,"SELECT payload FROM challenge_puzzles WHERE day=$d",("$d",day)))
            if(cmd.ExecuteScalar() is string existing) return Database.Decode<Puzzle>(existing);
        var puzzle=Sudoku.Generate(Difficulty.Medium,token);
        using var conn=db.Open(); using var tx=conn.BeginTransaction();
        using (var add=Tx(conn,tx,"INSERT OR IGNORE INTO challenge_puzzles VALUES($d,$p)",("$d",day),("$p",Database.Encode(puzzle)))) add.ExecuteNonQuery();
        using var get=Tx(conn,tx,"SELECT payload FROM challenge_puzzles WHERE day=$d",("$d",day));
        var saved=Database.Decode<Puzzle>((string)get.ExecuteScalar()!); tx.Commit(); return saved;
    }

    public bool ChallengeWon(string name,string day)
    {
        using var c=db.Open(); using var cmd=Database.Command(c,"SELECT 1 FROM challenge_wins WHERE name=$n AND day=$d",("$n",name),("$d",day));
        return cmd.ExecuteScalar() is not null;
    }
    private static void PruneChallengeSessions(SqliteConnection c,SqliteTransaction tx)
    {
        string start=Day(DateTimeOffset.UtcNow)[..7]+"-01";
        using var cmd=Tx(c,tx,"DELETE FROM challenge_sessions WHERE day<$d",("$d",start));cmd.ExecuteNonQuery();
    }
    private WebGame? ChallengeSession(string name,string day)
    {
        using var c=db.Open(); using var cmd=Database.Command(c,"SELECT payload FROM challenge_sessions WHERE name=$n AND day=$d",("$n",name),("$d",day));
        return cmd.ExecuteScalar() is string payload ? Database.Decode<WebGame>(payload) : null;
    }
    private void SaveChallengeSession(string name,WebGame session,GameRecord? terminal,string? walletKind)
    {
        if(IsGuest(name)) throw new InvalidOperationException("Challenge requires an account.");
        string day=session.ChallengeDay!;
        using var c=db.Open(); using var tx=c.BeginTransaction();
        if(walletKind is not null && !Debit(c,tx,name,walletKind)) throw new InvalidOperationException("Недостатньо ресурсів.");
        if(terminal is not null)
        {
            using var add=Tx(c,tx,"INSERT OR IGNORE INTO challenge_attempts VALUES($n,$id,$d,$o,$e,$p)",
                ("$n",name),("$id",terminal.Id.ToString()),("$d",day),("$o",terminal.Outcome),
                ("$e",terminal.Ended.ToUniversalTime().ToString("O")),("$p",Database.Encode(terminal)));
            if(add.ExecuteNonQuery()==1 && terminal.Outcome=="completed")
            {
                var audit=session.Audit??new MoveAudit(); bool flagged=audit.Flag(terminal.Game);
                using(var log=Tx(c,tx,"INSERT OR IGNORE INTO speed_audit VALUES($n,$id,$m,$f,$flag)",("$n",name),("$id",terminal.Id.ToString()),("$m",audit.Moves),("$f",audit.FastIntervals),("$flag",flagged?1:0))) log.ExecuteNonQuery();
                string endedDay=Day(terminal.Ended);
                if(!flagged && endedDay[..7]==day[..7] && string.CompareOrdinal(endedDay,day)>=0)
                {
                using var win=Tx(c,tx,"INSERT OR IGNORE INTO challenge_wins VALUES($n,$d)",("$n",name),("$d",day));
                if(win.ExecuteNonQuery()==1)
                {
                    AddPack(c,tx,name,day,"challenge",0,1,2);
                    var date=ParseDay(day); string month=MonthKey(date);
                    using var cup=Tx(c,tx,"""
                        INSERT OR IGNORE INTO monthly_cups(name,month)
                        SELECT $n,$m WHERE (SELECT COUNT(*) FROM challenge_wins WHERE name=$n AND day >= $start AND day < $end)=$days
                        """,("$n",name),("$m",month),("$start",month+"-01"),
                        ("$end",DateKey(new DateOnly(date.Year,date.Month,1).AddMonths(1))),("$days",DateTime.DaysInMonth(date.Year,date.Month)));
                    cup.ExecuteNonQuery();
                }
                }
            }
        }
        if(!ChallengeAllowed(day)){tx.Commit();return;}
        using var save=Tx(c,tx,"INSERT INTO challenge_sessions VALUES($n,$d,$p) ON CONFLICT(name,day) DO UPDATE SET payload=excluded.payload",
            ("$n",name),("$d",day),("$p",Database.Encode(session)));
        save.ExecuteNonQuery(); tx.Commit();
    }
    private static List<string> Cups(SqliteConnection c,string name)
    {
        using var cmd=Database.Command(c,"SELECT month FROM monthly_cups WHERE name=$n ORDER BY month DESC",("$n",name));
        using var rows=cmd.ExecuteReader(); var cups=new List<string>(); while(rows.Read()) cups.Add(rows.GetString(0)); return cups;
    }
    public ChallengeCalendar? Calendar(string name,string? month=null)
    {
        lock(Gate){using var cleanup=db.Open();using var tx=cleanup.BeginTransaction();PruneChallengeSessions(cleanup,tx);tx.Commit();}
        string today=Day(DateTimeOffset.UtcNow); month??=today[..7];
        if(!ParseDate(month+"-01",out var start) || string.CompareOrdinal(month,today[..7])>0 || start.Year<2000) return null;
        string end=DateKey(start.AddMonths(1)); var days=new List<ChallengeDay>();
        using var c=db.Open(); var states=new Dictionary<string,string>();
        using(var cmd=Database.Command(c,"""
            SELECT day,CASE WHEN EXISTS(SELECT 1 FROM challenge_wins w WHERE w.name=s.name AND w.day=s.day) THEN 'won'
              WHEN json_extract(payload,'$.game.completed')=1 OR json_extract(payload,'$.game.outOfLives')=1 THEN 'retry'
              ELSE 'started' END FROM challenge_sessions s WHERE name=$n AND day >= $start AND day < $end
            UNION ALL SELECT day,'won' FROM challenge_wins WHERE name=$n AND day >= $start AND day < $end
            """,("$n",name),("$start",DateKey(start)),("$end",end)))
        using(var rows=cmd.ExecuteReader()) while(rows.Read()) states[rows.GetString(0)]=rows.GetString(1);
        for(var d=start;d<start.AddMonths(1);d=d.AddDays(1))
        {
            string key=DateKey(d);
            string status=states.GetValueOrDefault(key,"available");
            if(status!="won") status=string.CompareOrdinal(key,today)>0?"future":month!=today[..7]?"missed":status;
            days.Add(new(key,status));
        }
        return new(month,today,days,Cups(c,name));
    }

    public string? ResolvePublicName(string user)
    {
        if(user.Length>80) return null;
        using var c=db.Open(); using var cmd=Database.Command(c,"""
            SELECT name FROM accounts WHERE (instr(name,'@')=0 AND name=lower($u))
              OR (instr(name,'@')>0 AND 'Гравець-'||substr(hex(salt),1,12)=$u) LIMIT 1
            """,("$u",user));
        return cmd.ExecuteScalar() as string;
    }
    public PublicProfile Profile(string name,int page=0,Difficulty? difficulty=null)
    {
        using var c=db.Open();
        string label;
        using(var cmd=Database.Command(c,"SELECT salt FROM accounts WHERE name=$n",("$n",name)))
            label=PublicName(name,(byte[])cmd.ExecuteScalar()!);
        int games=0,wins=0,perfect=0,streak=0,best=0,dailyWins=0; long score=0;
        var perLevel=Enum.GetValues<Difficulty>().Select(d=>new LevelStats(d,0,0,0,0,0,0)).ToArray();
        using(var cmd=Database.Command(c,"""
            SELECT outcome,score,CASE WHEN outcome='completed' AND coalesce(json_extract(payload,'$.game.mistakes'),0)=0
              AND coalesce(json_extract(payload,'$.game.hints'),0)=0 AND coalesce(json_extract(payload,'$.game.candidateUses'),0)=0
              AND coalesce(json_extract(payload,'$.game.autoFilled'),0)=0 AND coalesce(json_extract(payload,'$.game.lifeRestores'),0)=0
              THEN 1 ELSE 0 END,json_extract(payload,'$.game.puzzle.difficulty') FROM history WHERE name=$n ORDER BY julianday(json_extract(payload,'$.ended')),id
            """,("$n",name)))
        using(var rows=cmd.ExecuteReader()) while(rows.Read())
        {
            games++; score+=rows.GetInt64(1); perfect+=rows.GetInt32(2);
            bool won=rows.GetString(0)=="completed";
            if(won) { wins++; best=Math.Max(best,++streak); } else streak=0;
            int i=rows.GetInt32(3);if(i<0||i>=perLevel.Length)continue;
            var old=perLevel[i];int current=won?old.CurrentStreak+1:0;
            perLevel[i]=old with {Games=old.Games+1,Wins=old.Wins+(won?1:0),PerfectWins=old.PerfectWins+rows.GetInt32(2),
                Score=old.Score+rows.GetInt64(1),CurrentStreak=current,BestStreak=Math.Max(old.BestStreak,current)};
        }
        using(var cmd=Database.Command(c,"SELECT COUNT(*) FROM challenge_wins WHERE name=$n",("$n",name))) dailyWins=Convert.ToInt32(cmd.ExecuteScalar());
        var history=new List<PublicGame>();
        using(var cmd=Database.Command(c,"""
            SELECT payload,day FROM (
              SELECT payload,NULL day,julianday(json_extract(payload,'$.ended')) finished,id FROM history WHERE name=$n
              UNION ALL SELECT payload,day,julianday(ended) finished,id FROM challenge_attempts WHERE name=$n
            ) WHERE ($d IS NULL OR json_extract(payload,'$.game.puzzle.difficulty')=$d) ORDER BY finished DESC,id LIMIT 51 OFFSET $offset
            """,("$n",name),("$offset",page*50),("$d",difficulty is null?null:(int)difficulty.Value)))
        using(var rows=cmd.ExecuteReader()) while(rows.Read())
        {
            var r=Database.Decode<GameRecord>(rows.GetString(0)); var g=r.Game; string? day=rows.IsDBNull(1)?null:rows.GetString(1);
            history.Add(new(r.Ended,day is null?"regular":"challenge",r.Source,g.Puzzle.Difficulty,r.Outcome,g.Seconds,r.Score,g.Mistakes,g.Hints,g.CandidateUses,g.AutoFilled,g.LifeRestores,day));
        }
        bool more=history.Count>50; if(more)history.RemoveAt(50);
        return new(label,games,wins,perfect,streak,best,score,dailyWins,Cups(c,name),history,more,perLevel.ToList());
    }
}
