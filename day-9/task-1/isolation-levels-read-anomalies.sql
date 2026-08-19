-- SETUP (run once, either session)
IF OBJECT_ID('dbo.IsolationDemo') IS NOT NULL DROP TABLE dbo.IsolationDemo;
CREATE TABLE dbo.IsolationDemo (
    Id INT PRIMARY KEY,
    AccountName VARCHAR(50) NOT NULL,
    Balance INT NOT NULL
);
INSERT INTO dbo.IsolationDemo (Id, AccountName, Balance) VALUES
    (1, 'Acct-A', 1000),
    (2, 'Acct-B', 2000);

-- =====================================================================
-- 1. DIRTY READ -- reproduced under READ UNCOMMITTED
-- =====================================================================

-- SESSION 1
BEGIN TRANSACTION;
UPDATE dbo.IsolationDemo SET Balance = 9999 WHERE Id = 1;
-- do not commit yet

-- SESSION 2
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
SELECT Id, AccountName, Balance FROM dbo.IsolationDemo WHERE Id = 1;
-- returns Balance = 9999 (uncommitted value from Session 1)

-- SESSION 1
ROLLBACK TRANSACTION;
SELECT Balance FROM dbo.IsolationDemo WHERE Id = 1; -- back to 1000

-- =====================================================================
-- 1b. DIRTY READ PREVENTED -- READ COMMITTED (next level up)
-- =====================================================================

-- SESSION 1
BEGIN TRANSACTION;
UPDATE dbo.IsolationDemo SET Balance = 8888 WHERE Id = 1;
-- do not commit yet

-- SESSION 2
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
SELECT Id, AccountName, Balance FROM dbo.IsolationDemo WHERE Id = 1;
-- never returns 8888; returns the last committed value (1000)

-- SESSION 1
ROLLBACK TRANSACTION;

-- =====================================================================
-- 2. NON-REPEATABLE READ -- reproduced under READ COMMITTED
-- =====================================================================

-- SESSION 1
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
BEGIN TRANSACTION;
SELECT Balance FROM dbo.IsolationDemo WHERE Id = 2; -- first read

-- SESSION 2
BEGIN TRANSACTION;
UPDATE dbo.IsolationDemo SET Balance = 2500 WHERE Id = 2;
COMMIT TRANSACTION;

-- SESSION 1
SELECT Balance FROM dbo.IsolationDemo WHERE Id = 2; -- second read, value changed
COMMIT TRANSACTION;

-- =====================================================================
-- 2b. NON-REPEATABLE READ PREVENTED -- REPEATABLE READ
-- =====================================================================

-- SESSION 1
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
BEGIN TRANSACTION;
SELECT Balance FROM dbo.IsolationDemo WHERE Id = 2; -- first read

-- SESSION 2
UPDATE dbo.IsolationDemo SET Balance = 3000 WHERE Id = 2;
-- blocks until Session 1 ends its transaction

-- SESSION 1
SELECT Balance FROM dbo.IsolationDemo WHERE Id = 2; -- second read, unchanged
COMMIT TRANSACTION;
-- Session 2's UPDATE unblocks and commits here

-- =====================================================================
-- 3. PHANTOM READ -- reproduced under REPEATABLE READ
-- =====================================================================

-- SESSION 1
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
BEGIN TRANSACTION;
SELECT Id, Balance FROM dbo.IsolationDemo WHERE Balance > 2000; -- first predicate read

-- SESSION 2
INSERT INTO dbo.IsolationDemo (Id, AccountName, Balance) VALUES (3, 'Acct-C', 5000);
-- inserts and commits immediately, not blocked

-- SESSION 1
SELECT Id, Balance FROM dbo.IsolationDemo WHERE Balance > 2000; -- second predicate read, extra row appears
COMMIT TRANSACTION;

-- =====================================================================
-- 3b. PHANTOM READ PREVENTED -- SERIALIZABLE
-- =====================================================================

-- SESSION 1
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
SELECT Id, Balance FROM dbo.IsolationDemo WHERE Balance > 2000; -- first predicate read

-- SESSION 2
INSERT INTO dbo.IsolationDemo (Id, AccountName, Balance) VALUES (4, 'Acct-D', 6000);
-- blocks until Session 1 ends its transaction (range lock)

-- SESSION 1
SELECT Id, Balance FROM dbo.IsolationDemo WHERE Balance > 2000; -- second predicate read, identical to first
COMMIT TRANSACTION;
-- Session 2's INSERT unblocks and commits here

-- CLEANUP
DROP TABLE dbo.IsolationDemo;
