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
