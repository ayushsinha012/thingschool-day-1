# Day 9 — Reproduce and resolve a deadlock

## Exercise

Force a classic two-resource deadlock across two SQL sessions: Session 1
locks resource A then tries to lock resource B, while Session 2 locks
resource B then tries to lock resource A. Capture the actual deadlock
victim error and, where possible, the deadlock graph. Then fix it by
making both sessions acquire locks in the same order, and prove the fix
removes the deadlock.

## Azure SQL Database

- **Database:** `thinkschool-day7`
- **Resource group:** `thinkschool-rg`
- **Region:** Central India (`centralindia`)

Reused the existing Azure SQL Database from prior Day 9 work rather than
provisioning new infrastructure. No passwords, connection strings, access
tokens, or other secrets are included anywhere in this document or in
any file under `day-9/task-2/`. Connections were made with an Azure AD
access token for the signed-in `az login` identity; login identifiers
were redacted from the captured deadlock graph before it was committed.

## Deadlock setup

Two independently lockable rows in a small demo table,
`dbo.DeadlockDemo`:

```sql
CREATE TABLE dbo.DeadlockDemo (
    Id INT PRIMARY KEY,
    ResourceName VARCHAR(50) NOT NULL,
    Value INT NOT NULL
);
INSERT INTO dbo.DeadlockDemo (Id, ResourceName, Value) VALUES
    (1, 'Resource-A', 100),
    (2, 'Resource-B', 200);
```

`Id = 1` (Resource-A) and `Id = 2` (Resource-B) are the two resources
locked in opposite order by the two sessions. Full setup, including the
Extended Events session used for capture, is in
[`setup.sql`](./setup.sql).

## Session 1

SQL (also in [`session1.sql`](./session1.sql)):

```sql
BEGIN TRANSACTION;
UPDATE dbo.DeadlockDemo SET Value = Value + 1 WHERE Id = 1;
WAITFOR DELAY '00:00:05';
UPDATE dbo.DeadlockDemo SET Value = Value + 1 WHERE Id = 2;
COMMIT TRANSACTION;
```

**Actual result:** locked `Id=1` at `04:53:59.016`, waited, then
attempted `Id=2` at `04:54:04.023`. Blocked until `04:54:07.824`, then
acquired the lock and committed at `04:54:07.831`. Session 1 was **not**
the victim — it committed successfully.

## Session 2

SQL (also in [`session2.sql`](./session2.sql)):

```sql
BEGIN TRANSACTION;
UPDATE dbo.DeadlockDemo SET Value = Value + 1 WHERE Id = 2;
WAITFOR DELAY '00:00:05';
UPDATE dbo.DeadlockDemo SET Value = Value + 1 WHERE Id = 1;
COMMIT TRANSACTION;
```

**Actual result:** locked `Id=2` at `04:53:59.111`, waited, then
attempted `Id=1` at `04:54:04.117`. Instead of blocking indefinitely,
this statement returned an error — Session 2 was chosen as the deadlock
victim (see below).

Both sessions were launched as two independent `sqlcmd` processes at the
same instant (`sqlcmd -i session1.sql & sqlcmd -i session2.sql & wait`),
so the two `BEGIN TRANSACTION` blocks and their locks were genuinely
concurrent, not simulated in one connection.

## Deadlock result

Session 2 returned this actual error, verbatim:

```
Msg 1205, Level 13, State 72, Server thinkschool-day7-sql-0c0dda, Line 8
Transaction (Process ID 89) was deadlocked on lock resources with
another process and has been chosen as the deadlock victim. Rerun the
transaction.
```

Session 2 (SPID 89) is the deadlock victim. Session 1 committed
successfully once Session 2's rollback released the lock it was waiting
on.

**Why Session 2 became the victim:** SQL Server's deadlock monitor
prefers to kill the cheaper transaction (by log bytes written) when
`DEADLOCK_PRIORITY` is unset on both sides (it was, on both). The
deadlock graph (below) shows Session 2's process at `logused="296"`
versus Session 1's `logused="400"` — Session 2 had done less work, so it
was the cheaper transaction to roll back, and SQL Server chose it. This
matches the actual result.

## Deadlock graph / evidence

Azure SQL Database does not expose the server-scope
`sqlserver.xml_deadlock_report` event to database-scoped Extended Events
sessions:

```
Msg 25743, Level 16, State 1
The event 'sqlserver.xml_deadlock_report' is not available for Azure SQL Database.
```

The Azure SQL-supported equivalent, found via `sys.dm_xe_objects`, is
`sqlserver.database_xml_deadlock_report`:

```sql
CREATE EVENT SESSION CaptureDeadlocks ON DATABASE
ADD EVENT sqlserver.database_xml_deadlock_report
ADD TARGET package0.ring_buffer
WITH (STARTUP_STATE = ON);
ALTER EVENT SESSION CaptureDeadlocks ON DATABASE STATE = START;
```

The full deadlock graph was then read back from the ring buffer target
and saved as [`deadlock-graph.xml`](./deadlock-graph.xml) (login
identifiers redacted):

```sql
SELECT CAST(t.target_data AS XML) AS deadlock_ring_buffer
FROM sys.dm_xe_database_session_targets t
JOIN sys.dm_xe_database_sessions s ON s.address = t.event_session_address
WHERE s.name = 'CaptureDeadlocks' AND t.target_name = 'ring_buffer';
```

Key facts read directly from that captured graph:

- `<victim-list><victimProcess id="process21b40626c58"/>` matches
  `spid="89"`, whose `<inputbuf>` is the exact text of `session2.sql` —
  confirming Session 2 as the victim, independent of the error text.
- The surviving process, `spid="87"`, has an `<inputbuf>` matching
  `session1.sql` exactly.
- `<resource-list>` shows two commit-duration key locks on the same
  index (`PK__Deadlock__...`): the lock on `Id=1`'s key is owned by
  spid 87 (`X` mode) and waited on by spid 89 (`S` mode); the lock on
  `Id=2`'s key is owned by spid 89 (`X` mode) and waited on by spid 87
  (`S` mode) — the textbook circular wait.

## Fix

Both sessions now acquire `Id = 1` before `Id = 2` — the same order —
in [`session1-fixed.sql`](./session1-fixed.sql) and
[`session2-fixed.sql`](./session2-fixed.sql), which are identical:

```sql
BEGIN TRANSACTION;
UPDATE dbo.DeadlockDemo SET Value = Value + 1 WHERE Id = 1;
WAITFOR DELAY '00:00:05';
UPDATE dbo.DeadlockDemo SET Value = Value + 1 WHERE Id = 2;
COMMIT TRANSACTION;
```

Consistent lock ordering prevents the circular wait because a cycle
requires each transaction to hold a resource the other is waiting for;
if every transaction acquires the same shared resources in the same
order, the second transaction to arrive can only ever be waiting behind
the first one on that same resource, never ahead of it on one resource
while behind it on another — so the wait graph can no longer close into
a loop.

## Validation after fix

Both fixed scripts were launched concurrently the same way as the buggy
version. Actual output:

**Session 2 fixed** (arrived first, got the `Id=1` lock immediately):
```
S2F: start                2026-08-19 04:55:02.407
S2F: locked Id=1           2026-08-19 04:55:02.407
S2F: attempting Id=2       2026-08-19 04:55:07.413
S2F: locked Id=2           2026-08-19 04:55:07.413
S2F: committed             2026-08-19 04:55:07.422
```

**Session 1 fixed** (blocked in an ordinary lock wait on `Id=1` until
Session 2 fixed committed, then proceeded):
```
S1F: start                2026-08-19 04:55:02.411
S1F: locked Id=1           2026-08-19 04:55:07.422
S1F: attempting Id=2       2026-08-19 04:55:12.435
S1F: locked Id=2           2026-08-19 04:55:12.435
S1F: committed             2026-08-19 04:55:12.443
```

No `Msg 1205` occurred; both `sqlcmd` processes exited with code 0.
Session 1 fixed's wait on `Id=1` (04:55:02.411 → 04:55:07.422) was an
ordinary lock wait resolved by queuing, not a cycle.

```sql
SELECT * FROM dbo.DeadlockDemo ORDER BY Id;
-- Id=1  Resource-A  103
-- Id=2  Resource-B  203

SELECT COUNT(*) AS blocked_requests FROM sys.dm_exec_requests WHERE blocking_session_id <> 0;
-- blocked_requests = 0
```

Both rows show two more increments than after the buggy run
(`101`/`201` → `103`/`203`), one full increment per fixed transaction,
confirming both fixed transactions committed completely, and zero
sessions were left blocked.

## Exercise requirement

- **Reproduction scripts:** [`session1.sql`](./session1.sql),
  [`session2.sql`](./session2.sql) (setup in
  [`setup.sql`](./setup.sql); consolidated view in
  [`deadlock-and-fix.sql`](./deadlock-and-fix.sql))
- **Deadlock graph or victim message:** `Msg 1205` (Session 2, SPID 89)
  quoted above verbatim, plus the captured graph in
  [`deadlock-graph.xml`](./deadlock-graph.xml)
- **Fixed scripts:** [`session1-fixed.sql`](./session1-fixed.sql),
  [`session2-fixed.sql`](./session2-fixed.sql)
- **One-line explanation of why the fix works:** both sessions now
  acquire the same two resources in the same order, so the wait graph
  between them can only ever be a line, never a cycle.

## Actual execution / validation

| Step | Result |
|---|---|
| Connected to Azure SQL (`thinkschool-day7`) via AAD token, two independent sessions | PASS |
| Deadlock reproduced (buggy Session 1 / Session 2, opposite lock order) | PASS |
| Actual `Msg 1205` deadlock victim error captured (Session 2, SPID 89) | PASS |
| Deadlock graph captured via database-scoped Extended Events (`database_xml_deadlock_report`) | PASS |
| Victim identified from actual execution and cross-confirmed by the graph | PASS |
| Fix applied (consistent lock order, `Id=1` before `Id=2` in both sessions) | PASS |
| Fixed version executed concurrently with no deadlock, both sessions committed | PASS |
| Post-fix validation query showed 0 blocked requests and correct final row values | PASS |
| Demo table and Extended Events session cleaned up, verified dropped | PASS |

## Remaining Day 9 work

This document covers Day 9 Task 2 only. Day 9 Task 1 (isolation levels
and read anomalies) is separately documented in
[`../task-1/README.md`](../task-1/README.md) and is not re-verified
here. No other Day 9 tasks exist in this repository at this time; if
additional Day 9 exercises are added later, their status is not
addressed by this document.
