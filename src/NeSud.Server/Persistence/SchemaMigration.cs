using Microsoft.Data.Sqlite;
using NeSud.Storage;

namespace NeSud.Server;

public sealed partial class ServerStore
{
    // Version 1: relational history, puzzle catalog, reward cards and referral facts.
    private static void NormalizeSchema(SqliteConnection c, SqliteTransaction tx)
    {
        using var version = Tx(c, tx, "PRAGMA user_version");
        int current = Convert.ToInt32(version.ExecuteScalar());
        if (current > 1) throw new InvalidDataException("Unsupported server database schema version.");
        if (current == 1) return;

        using (var schema = Tx(c, tx, """
            ALTER TABLE history RENAME TO history_legacy;
            CREATE TABLE history_puzzles (
                puzzle_id INTEGER PRIMARY KEY,
                givens TEXT NOT NULL CHECK(json_valid(givens) AND json_array_length(givens)=81),
                solution TEXT NOT NULL CHECK(json_valid(solution) AND json_array_length(solution)=81),
                difficulty INTEGER NOT NULL,
                UNIQUE(givens,solution,difficulty));
            CREATE TABLE history (
                cursor INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL REFERENCES accounts(name),
                id TEXT NOT NULL,
                source TEXT NOT NULL CHECK(source IN ('web','desktop')),
                score INTEGER NOT NULL CHECK(score>=0),
                outcome TEXT NOT NULL CHECK(outcome IN ('completed','abandoned','defeated')),
                started TEXT NOT NULL,
                ended TEXT NOT NULL,
                challenge_day TEXT,
                puzzle_id INTEGER NOT NULL REFERENCES history_puzzles(puzzle_id),
                game TEXT NOT NULL CHECK(json_valid(game) AND json_type(game)='object' AND json_type(game,'$.puzzle') IS NULL),
                ended_jd REAL GENERATED ALWAYS AS (julianday(ended)) STORED NOT NULL,
                seconds INTEGER GENERATED ALWAYS AS (coalesce(json_extract(game,'$.seconds'),0)) STORED,
                mistakes INTEGER GENERATED ALWAYS AS (coalesce(json_extract(game,'$.mistakes'),0)) STORED,
                hints INTEGER GENERATED ALWAYS AS (coalesce(json_extract(game,'$.hints'),0)) STORED,
                rules INTEGER GENERATED ALWAYS AS (coalesce(json_extract(game,'$.rulesVersion'),0)) STORED,
                candidate_changes INTEGER GENERATED ALWAYS AS (coalesce(json_extract(game,'$.candidateChanges'),0)) STORED,
                perfect INTEGER GENERATED ALWAYS AS (outcome='completed' AND mistakes=0 AND hints=0
                    AND coalesce(json_extract(game,'$.candidateUses'),0)=0
                    AND coalesce(json_extract(game,'$.autoFilled'),0)=0
                    AND coalesce(json_extract(game,'$.lifeRestores'),0)=0) STORED,
                UNIQUE(name,id));
            """)) schema.ExecuteNonQuery();

        CopyHistory(c, tx, "SELECT rowid AS cursor,name,id,source,score,outcome,payload FROM history_legacy");
        using (var check = Tx(c, tx, """
            SELECT COUNT(*) FROM history_legacy old
            LEFT JOIN history h ON h.cursor=old.rowid AND h.name=old.name AND h.id=old.id
            WHERE h.cursor IS NULL
            """))
            if (Convert.ToInt64(check.ExecuteScalar()) != 0) throw new InvalidDataException("History migration lost records.");

        using (var schema = Tx(c, tx, """
            DROP TABLE history_legacy;
            CREATE VIEW history_records AS
                SELECT h.cursor,h.name,h.id,
                    json_object('id',h.id,'started',h.started,'ended',h.ended,'source',h.source,
                        'outcome',h.outcome,'score',h.score,'challengeDay',h.challenge_day,
                        'game',json(json_set(h.game,'$.puzzle',json_object('givens',json(p.givens),
                            'solution',json(p.solution),'difficulty',p.difficulty)))) AS payload
                FROM history h JOIN history_puzzles p ON p.puzzle_id=h.puzzle_id;
            CREATE INDEX ix_history_cursor ON history(name,cursor);
            CREATE INDEX ix_history_order ON history(name,ended_jd DESC,id);
            CREATE INDEX ix_history_daily ON history(name,ended_jd,puzzle_id) WHERE outcome='completed' AND challenge_day IS NULL;
            CREATE INDEX ix_history_rank_web ON history(name,score) WHERE source='web' AND outcome='completed';
            CREATE INDEX ix_history_rank_all ON history(name,score) WHERE outcome='completed';
            CREATE INDEX ix_history_week_web ON history(ended_jd,name,score) WHERE source='web' AND outcome='completed';
            CREATE INDEX ix_history_compare_web ON history(puzzle_id,rules,seconds,score,name) WHERE source='web' AND outcome='completed';
            CREATE INDEX ix_history_puzzle ON history(puzzle_id);
            CREATE INDEX ix_history_web_order ON history(name,ended_jd,id) WHERE source='web';
            CREATE INDEX ix_history_puzzle_difficulty ON history_puzzles(difficulty,puzzle_id);
            CREATE INDEX ix_speed_flagged ON speed_audit(name,id) WHERE flagged=1;
            CREATE INDEX ix_desktop_drafts_updated ON desktop_drafts(name,updated DESC);
            CREATE INDEX ix_tokens_name ON tokens(name);
            CREATE INDEX ix_challenge_wins_day ON challenge_wins(day);
            CREATE INDEX ix_tournament_results_name ON tournament_results(name);
            CREATE INDEX ix_profiles_referrer ON account_profiles(referred_by);
            CREATE UNIQUE INDEX ux_friend_pair ON friendships(min(sender,recipient),max(sender,recipient));
            CREATE TABLE daily_pack_cards (
                name TEXT NOT NULL, day TEXT NOT NULL, milestone TEXT NOT NULL, slot INTEGER NOT NULL,
                card INTEGER NOT NULL CHECK(card BETWEEN 0 AND 2),
                kind TEXT NOT NULL CHECK(kind IN ('hint','candidates')),
                amount INTEGER NOT NULL CHECK(amount>0),
                PRIMARY KEY(name,day,milestone,slot,card),
                FOREIGN KEY(name,day,milestone,slot) REFERENCES daily_packs(name,day,milestone,slot) ON DELETE CASCADE
            ) WITHOUT ROWID;
            INSERT INTO daily_pack_cards(name,day,milestone,slot,card,kind,amount)
            SELECT p.name,p.day,p.milestone,p.slot,j.key,
                CASE json_extract(j.value,'$.kind') WHEN 'life' THEN 'hint' ELSE json_extract(j.value,'$.kind') END,
                json_extract(j.value,'$.amount')
            FROM daily_packs p,json_each(p.options) j;
            """)) schema.ExecuteNonQuery();

        using (var check = Tx(c, tx, """
            SELECT COUNT(*) FROM daily_packs p WHERE
            (SELECT COUNT(*) FROM daily_pack_cards k WHERE (k.name,k.day,k.milestone,k.slot)=(p.name,p.day,p.milestone,p.slot))<>3
            OR (p.chosen IS NOT NULL AND p.chosen NOT BETWEEN 0 AND 2)
            """))
            if (Convert.ToInt64(check.ExecuteScalar()) != 0) throw new InvalidDataException("Invalid legacy reward pack.");

        using (var check = Tx(c, tx, """
            SELECT COUNT(*) FROM referral_rewards r LEFT JOIN account_profiles p ON p.name=r.invited
            WHERE p.name IS NULL OR p.referred_by IS NOT r.inviter
            """))
            if (Convert.ToInt64(check.ExecuteScalar()) != 0) throw new InvalidDataException("Conflicting legacy referral facts.");

        using (var schema = Tx(c, tx, """
            ALTER TABLE daily_packs DROP COLUMN options;
            CREATE TRIGGER daily_pack_choice_guard BEFORE UPDATE OF chosen ON daily_packs
                WHEN NEW.chosen IS NOT NULL AND NOT EXISTS(
                    SELECT 1 FROM daily_pack_cards k WHERE
                    (k.name,k.day,k.milestone,k.slot,k.card)=(NEW.name,NEW.day,NEW.milestone,NEW.slot,NEW.chosen))
                BEGIN SELECT RAISE(ABORT,'Invalid reward card'); END;
            ALTER TABLE referral_rewards RENAME TO referral_rewards_legacy;
            CREATE TABLE referral_rewards (
                invited TEXT PRIMARY KEY NOT NULL REFERENCES account_profiles(name),
                rewarded_at INTEGER NOT NULL);
            INSERT INTO referral_rewards SELECT invited,rewarded_at FROM referral_rewards_legacy;
            DROP TABLE referral_rewards_legacy;
            CREATE TRIGGER immutable_referrer BEFORE UPDATE OF referred_by ON account_profiles
                WHEN OLD.referred_by IS NOT NEW.referred_by
                BEGIN SELECT RAISE(ABORT,'Referrer is immutable'); END;
            PRAGMA user_version=1;
            """)) schema.ExecuteNonQuery();
        using var foreignKeys = Tx(c, tx, "PRAGMA foreign_key_check");
        using var violations = foreignKeys.ExecuteReader();
        if (violations.Read()) throw new InvalidDataException("Foreign key violation in migrated server database.");
    }

    // Source SQL is internal, never supplied by clients. A NULL cursor allocates a new stable sync cursor.
    private static void CopyHistory(SqliteConnection c, SqliteTransaction tx, string sourceSql)
    {
        using (var puzzles = Tx(c, tx, """
            INSERT INTO history_puzzles(givens,solution,difficulty)
            SELECT DISTINCT json_extract(payload,'$.game.puzzle.givens'),
                json_extract(payload,'$.game.puzzle.solution'),json_extract(payload,'$.game.puzzle.difficulty')
            FROM (
            """ + "\n" + sourceSql + """
            ) WHERE 1
            ON CONFLICT(givens,solution,difficulty) DO NOTHING
            """)) puzzles.ExecuteNonQuery();
        using var records = Tx(c, tx, """
            INSERT INTO history(cursor,name,id,source,score,outcome,started,ended,challenge_day,puzzle_id,game)
            SELECT s.cursor,s.name,s.id,s.source,s.score,s.outcome,
                json_extract(s.payload,'$.started'),json_extract(s.payload,'$.ended'),
                json_extract(s.payload,'$.challengeDay'),
                (SELECT p.puzzle_id FROM history_puzzles p
                 WHERE p.givens=json_extract(s.payload,'$.game.puzzle.givens')
                   AND p.solution=json_extract(s.payload,'$.game.puzzle.solution')
                   AND p.difficulty=json_extract(s.payload,'$.game.puzzle.difficulty')),
                json_remove(json_extract(s.payload,'$.game'),'$.puzzle')
            FROM (
            """ + "\n" + sourceSql + """
            ) s
            """);
        records.ExecuteNonQuery();
    }
}
