-- SETUP (run once, either session)
IF OBJECT_ID('dbo.DeadlockDemo') IS NOT NULL DROP TABLE dbo.DeadlockDemo;
CREATE TABLE dbo.DeadlockDemo (
    Id INT PRIMARY KEY,
    ResourceName VARCHAR(50) NOT NULL,
    Value INT NOT NULL
);
INSERT INTO dbo.DeadlockDemo (Id, ResourceName, Value) VALUES
    (1, 'Resource-A', 100),
    (2, 'Resource-B', 200);

IF EXISTS (SELECT 1 FROM sys.database_event_sessions WHERE name = 'CaptureDeadlocks')
    DROP EVENT SESSION CaptureDeadlocks ON DATABASE;
CREATE EVENT SESSION CaptureDeadlocks ON DATABASE
ADD EVENT sqlserver.database_xml_deadlock_report
ADD TARGET package0.ring_buffer
WITH (STARTUP_STATE = ON);
ALTER EVENT SESSION CaptureDeadlocks ON DATABASE STATE = START;

-- =====================================================================
-- DEADLOCK REPRODUCTION -- inconsistent lock order across two sessions
-- =====================================================================

-- SESSION 1 (run concurrently with Session 2)
SET NOCOUNT ON;
BEGIN TRANSACTION;
UPDATE dbo.DeadlockDemo SET Value = Value + 1 WHERE Id = 1;
WAITFOR DELAY '00:00:05';
UPDATE dbo.DeadlockDemo SET Value = Value + 1 WHERE Id = 2;
COMMIT TRANSACTION;

-- SESSION 2 (run concurrently with Session 1)
SET NOCOUNT ON;
BEGIN TRANSACTION;
UPDATE dbo.DeadlockDemo SET Value = Value + 1 WHERE Id = 2;
WAITFOR DELAY '00:00:05';
UPDATE dbo.DeadlockDemo SET Value = Value + 1 WHERE Id = 1;
COMMIT TRANSACTION;

-- one of the two sessions above returns:
-- Msg 1205, Level 13, State 72
-- Transaction (Process ID 89) was deadlocked on lock resources with
-- another process and has been chosen as the deadlock victim. Rerun the
-- transaction.

-- =====================================================================
-- DEADLOCK DIAGNOSIS -- deadlock graph captured by the XE session above
-- =====================================================================

SELECT CAST(t.target_data AS XML) AS deadlock_ring_buffer
FROM sys.dm_xe_database_session_targets t
JOIN sys.dm_xe_database_sessions s ON s.address = t.event_session_address
WHERE s.name = 'CaptureDeadlocks' AND t.target_name = 'ring_buffer';

-- =====================================================================
-- FIX -- both sessions acquire locks in the same order (Id=1 then Id=2)
-- =====================================================================

-- SESSION 1 FIXED (run concurrently with Session 2 fixed)
SET NOCOUNT ON;
BEGIN TRANSACTION;
UPDATE dbo.DeadlockDemo SET Value = Value + 1 WHERE Id = 1;
WAITFOR DELAY '00:00:05';
UPDATE dbo.DeadlockDemo SET Value = Value + 1 WHERE Id = 2;
COMMIT TRANSACTION;

-- SESSION 2 FIXED (run concurrently with Session 1 fixed)
SET NOCOUNT ON;
BEGIN TRANSACTION;
UPDATE dbo.DeadlockDemo SET Value = Value + 1 WHERE Id = 1;
WAITFOR DELAY '00:00:05';
UPDATE dbo.DeadlockDemo SET Value = Value + 1 WHERE Id = 2;
COMMIT TRANSACTION;

-- both sessions commit; the second one simply blocks on the Id=1 row
-- until the first commits, then proceeds -- no deadlock

-- =====================================================================
-- VALIDATION
-- =====================================================================

SELECT * FROM dbo.DeadlockDemo ORDER BY Id;
SELECT COUNT(*) AS blocked_requests FROM sys.dm_exec_requests WHERE blocking_session_id <> 0;

-- CLEANUP
ALTER EVENT SESSION CaptureDeadlocks ON DATABASE STATE = STOP;
DROP EVENT SESSION CaptureDeadlocks ON DATABASE;
DROP TABLE dbo.DeadlockDemo;
