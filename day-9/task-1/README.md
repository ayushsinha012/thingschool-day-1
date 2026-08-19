# Day 9 — Isolation levels + the read anomalies

> Open two sessions and reproduce a dirty read, a non-repeatable read,
> and a phantom read, then show which isolation level prevents each
> (READ UNCOMMITTED → SERIALIZABLE).

## Exercise

> Paste the two-session scripts for each anomaly and a short table:
> anomaly → lowest isolation level that prevents it.

The full, runnable script is
[`isolation-levels-read-anomalies.sql`](./isolation-levels-read-anomalies.sql)
in this same folder. This README documents what that script contains
and the actual results of running it, statement-by-statement, against
Azure SQL — nothing below is invented or backfilled from theory.

## Azure SQL Database

- **Database:** `thinkschool-day7`
- **Resource group:** `thinkschool-rg`
- **Region:** Central India (`centralindia`)

Reused the existing Day 7 Azure SQL Database rather than provisioning a
new resource. No credentials, passwords, connection strings, tokens, or
secrets of any kind are included in this document or the SQL file.

## How the two sessions were run

Two independent connections (one dedicated TDS session each) stood in
for "Session 1" and "Session 2", so `BEGIN TRANSACTION` and isolation
level state on one session was never shared with the other — the same
isolation as two separate SSMS query windows.

Test table, seeded with two synthetic rows and dropped again at the end
of the script:

```sql
CREATE TABLE dbo.IsolationDemo (
    Id INT PRIMARY KEY,
    AccountName VARCHAR(50) NOT NULL,
    Balance INT NOT NULL
);
-- (1, 'Acct-A', 1000), (2, 'Acct-B', 2000)
```

One relevant setting was checked directly rather than assumed:

```sql
SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME();
-- is_read_committed_snapshot_on = 1
```

`thinkschool-day7` has **READ_COMMITTED_SNAPSHOT ON** (the Azure SQL
default for new databases). That changes *how* READ COMMITTED prevents
the dirty read below — via row versioning instead of blocking — called
out explicitly where it applies.

## Dirty Read

**Session 1 SQL:**

```sql
BEGIN TRANSACTION;
UPDATE dbo.IsolationDemo SET Balance = 9999 WHERE Id = 1;
-- do not commit yet
```

**Session 2 SQL:**

```sql
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
SELECT Id, AccountName, Balance FROM dbo.IsolationDemo WHERE Id = 1;
```

- **Isolation level used:** READ UNCOMMITTED
- **Actual observed result:** Session 2 read `Balance = 9999` while
  Session 1's transaction was still open and uncommitted. Session 1
  then ran `ROLLBACK TRANSACTION;`, after which `Balance` was back to
  `1000` — confirming `9999` was never a committed value. This is a
  genuine dirty read.
- **Lowest isolation level that prevented it:** **READ COMMITTED**.
  Repeating the same steps with Session 2 set to
  `SET TRANSACTION ISOLATION LEVEL READ COMMITTED;` instead, its
  `SELECT` resolved in **97 ms** with `Balance = 1000` — the
  last-committed value — and never saw Session 1's uncommitted `8888`.
  (Because this database has RCSI on, the read is not blocked; it is
  served from the last committed row version instead. Either mechanism
  guarantees the uncommitted value is never returned.)

## Non-repeatable Read

**Session 1 SQL:**

```sql
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
BEGIN TRANSACTION;
SELECT Balance FROM dbo.IsolationDemo WHERE Id = 2; -- first read
-- ... later, same transaction ...
SELECT Balance FROM dbo.IsolationDemo WHERE Id = 2; -- second read
COMMIT TRANSACTION;
```

**Session 2 SQL:**

```sql
BEGIN TRANSACTION;
UPDATE dbo.IsolationDemo SET Balance = 2500 WHERE Id = 2;
COMMIT TRANSACTION;
```

- **First read result:** `Balance = 2000`
- **Second read result:** `Balance = 2500`
- **Actual observed behavior:** Session 2 committed its update between
  Session 1's two reads inside the same open transaction, and Session
  1's second read returned the new value. Two reads of the same row in
  one transaction returned different results — a genuine non-repeatable
  read.
- **Lowest isolation level that prevented it:** **REPEATABLE READ**.
  Repeating the same steps with Session 1 set to
  `SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;`, Session 2's
  `UPDATE` on that row **blocked** (still pending after a 2000 ms check)
  until Session 1 ran `COMMIT TRANSACTION;`; only then did Session 2's
  update unblock and complete (2163 ms total). Session 1's second read
  inside its transaction returned `2500` — identical to its first read.

## Phantom Read

**Session 1 SQL:**

```sql
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
BEGIN TRANSACTION;
SELECT Id, Balance FROM dbo.IsolationDemo WHERE Balance > 2000; -- first predicate read
-- ... later, same transaction ...
SELECT Id, Balance FROM dbo.IsolationDemo WHERE Balance > 2000; -- second predicate read
COMMIT TRANSACTION;
```

**Session 2 SQL:**

```sql
INSERT INTO dbo.IsolationDemo (Id, AccountName, Balance) VALUES (3, 'Acct-C', 5000);
```

- **First result set:** `[{Id: 2, Balance: 3000}]`
- **Second result set:** `[{Id: 2, Balance: 3000}, {Id: 3, Balance: 5000}]`
- **Actual observed phantom row:** `{Id: 3, Balance: 5000}` — Session 2's
  insert committed immediately (REPEATABLE READ locks only the rows it
  already read, not the range `Balance > 2000`), and it appeared in
  Session 1's second predicate query inside the same still-open
  transaction. That extra row is the phantom.
- **Lowest isolation level that prevented it:** **SERIALIZABLE**.
  Repeating the same steps with Session 1 set to
  `SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;`, Session 2's `INSERT`
  of a new row matching `Balance > 2000` **blocked** (still pending
  after a 2000 ms check) until Session 1 ran `COMMIT TRANSACTION;`;
  only then did it unblock and complete (2153 ms total). Session 1's
  second predicate read inside its transaction was identical to its
  first — no phantom.

## Isolation Level Summary

| Anomaly | Reproduced? | Lowest isolation level that prevents it |
|---|---|---|
| Dirty read | PASS | READ COMMITTED |
| Non-repeatable read | PASS | REPEATABLE READ |
| Phantom read | PASS | SERIALIZABLE |

## Two-Session Scripts

**Dirty read — Session 1:**
```sql
BEGIN TRANSACTION;
UPDATE dbo.IsolationDemo SET Balance = 9999 WHERE Id = 1;
ROLLBACK TRANSACTION;
```
**Dirty read — Session 2:**
```sql
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
SELECT Id, AccountName, Balance FROM dbo.IsolationDemo WHERE Id = 1;
```

**Non-repeatable read — Session 1:**
```sql
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
BEGIN TRANSACTION;
SELECT Balance FROM dbo.IsolationDemo WHERE Id = 2;
SELECT Balance FROM dbo.IsolationDemo WHERE Id = 2;
COMMIT TRANSACTION;
```
**Non-repeatable read — Session 2:**
```sql
BEGIN TRANSACTION;
UPDATE dbo.IsolationDemo SET Balance = 2500 WHERE Id = 2;
COMMIT TRANSACTION;
```

**Phantom read — Session 1:**
```sql
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
BEGIN TRANSACTION;
SELECT Id, Balance FROM dbo.IsolationDemo WHERE Balance > 2000;
SELECT Id, Balance FROM dbo.IsolationDemo WHERE Balance > 2000;
COMMIT TRANSACTION;
```
**Phantom read — Session 2:**
```sql
INSERT INTO dbo.IsolationDemo (Id, AccountName, Balance) VALUES (3, 'Acct-C', 5000);
```

The corresponding prevention runs (READ COMMITTED, REPEATABLE READ, and
SERIALIZABLE respectively) and the full setup/cleanup are all in
[`isolation-levels-read-anomalies.sql`](./isolation-levels-read-anomalies.sql).

## Actual Validation

- Azure SQL connection/execution — **PASS** (connected to
  `thinkschool-day7` via two independent sessions using an Azure AD
  access token for the signed-in `az login` identity; no credentials
  stored)
- Dirty read — **PASS** (Session 2 under READ UNCOMMITTED read the
  uncommitted `9999`)
- Non-repeatable read — **PASS** (Session 1 read `2000` then `2500`
  within one open transaction)
- Phantom read — **PASS** (Session 1's second predicate read picked up
  the new `Id = 3` row Session 2 inserted mid-transaction)
- Isolation-level prevention tests — **PASS** (READ COMMITTED stopped
  the dirty read, REPEATABLE READ stopped the non-repeatable read and
  measurably blocked Session 2 for ~2.2 s, SERIALIZABLE stopped the
  phantom read and measurably blocked Session 2's insert for ~2.2 s)
- Transactions safely completed — **PASS** (every transaction opened in
  the run was explicitly committed or rolled back; `dbo.IsolationDemo`
  was dropped at the end; a post-run check of
  `sys.dm_exec_requests` showed zero blocked sessions and
  `OBJECT_ID('dbo.IsolationDemo')` returned `NULL`)

## What was learned

Isolation levels trade correctness for concurrency, in a fixed order:

- **READ UNCOMMITTED** takes no read locks and reads no row versions —
  it will show you another transaction's uncommitted change, which can
  vanish (dirty read).
- **READ COMMITTED** only ever shows committed data, so the dirty read
  is gone — but it re-checks the data fresh on every statement, so the
  same transaction can see a row change value between two reads
  (non-repeatable read).
- **REPEATABLE READ** locks every row it has read for the rest of the
  transaction, so a re-read of the same row is guaranteed stable — but
  it doesn't lock the range a `WHERE` predicate covers, so a brand-new
  row matching that predicate can still appear (phantom read).
- **SERIALIZABLE** locks the whole range a query touches, so nothing
  else can insert into or change that range until the transaction ends
  — no phantoms either, at the cost of the most blocking of the four.

Each stronger level closes exactly the next anomaly down the list, and
each one does it by holding more, and longer, locks — concurrency goes
down as consistency goes up.

## Remaining Day 9 work

Day 9 Task 1 is complete. No remaining work for this task.
