# SQLite schema v1

Server history stores record metadata once, a reference to a deduplicated puzzle, and the remaining game snapshot. `history_records` reconstructs the existing JSON contract without keeping a second copy. The puzzle catalog has a unique tuple of givens, solution and difficulty; puzzle arrays and the opaque game/undo snapshot remain serialized. Indexed STORED generated columns are derived projections, not independently editable facts. Score is the recorded award, not recalculated on migration.

Reward cards are rows keyed by (name, day, milestone, slot, card), with a foreign key to their pack. The referral reward records only the rewarded account and timestamp; the inviter comes from the immutable profile referral. Other event and award tables retain their historical facts. Client state and active sessions remain JSON snapshots; this is not a claim of complete relational normalization of all serialized state.

Migration runs transactionally at startup, preserves old history rowids as AUTOINCREMENT sync cursors, preserves pack row order and chosen cards, migrates legacy challenge attempts, rejects conflicting legacy facts, and checks foreign keys. Back up the server database using SQLite backup (or stop the server and copy the database and its WAL if present) before upgrading. Old binaries cannot read the new schema. The old positional INSERT INTO history contract is no longer supported.

Comparison uses SQLite window functions and aggregates, returning one row. History date, puzzle, partial ranking, anti-audit and FK lookup indexes target current workloads. No measured speedup is claimed without a representative database and EXPLAIN QUERY PLAN. Run the smoke suite with dotnet run --project tests/NeSud.SmokeTests -- 1.
