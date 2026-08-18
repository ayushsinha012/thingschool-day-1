--------------------------------------------------------------------------
-- Day 8 — Clustered vs non-clustered indexes
--------------------------------------------------------------------------
-- Dialect: SQL Server / Azure SQL Database (T-SQL).
--
-- Indexes are the single biggest lever on read performance, and a tax on
-- writes. This script builds a ~100,000-row table as a heap (no indexes
-- at all), measures a baseline read cost with SET STATISTICS IO ON and
-- the actual execution plan (SET STATISTICS XML ON), then adds:
--   1. ONE clustered index  (on OrderDate)      -> speeds a date-range scan
--   2. Non-clustered index #1 (on CustomerId)   -> speeds a customer lookup
--   3. Non-clustered index #2 (on Status, OrderDate) -> speeds a status+date filter
-- re-measuring logical reads and the actual plan after each index, and
-- finally measures the write-side cost of maintaining all three indexes.
--
-- Reused the existing Day 7 Azure SQL Database rather than creating a new
-- Azure resource:
--
-- Executed against: Azure SQL Database `thinkschool-day7` on logical
-- server `thinkschool-day7-sql-0c0dda.database.windows.net`
-- (resource group `thinkschool-rg`, region `centralindia`), via an Azure
-- AD access token for the signed-in `az login` identity (that identity is
-- the server's Azure AD admin) — no SQL login/password used or stored.
-- Every measurement in this file's trailing comments and in the Day 8
-- report is genuine output from running this script against that
-- database, not invented.
--
-- This script owns its own table (dbo.OrderActivity) and does not touch
-- the Day 7 Authors/Quotes schema in the same database.
--------------------------------------------------------------------------


--------------------------------------------------------------------------
-- 1. Database / table setup
--------------------------------------------------------------------------
-- No USE/CREATE DATABASE: an Azure SQL connection is already scoped to a
-- single target database.

IF OBJECT_ID(N'dbo.OrderActivity', N'U') IS NOT NULL
    DROP TABLE dbo.OrderActivity;
GO

-- Deliberately created as a HEAP: no PRIMARY KEY / clustered index yet.
-- The whole point of the exercise is to measure a real "before" state
-- with no index of any kind, then add indexes one at a time.
CREATE TABLE dbo.OrderActivity
(
    OrderId     INT             IDENTITY (1, 1) NOT NULL,
    CustomerId  INT             NOT NULL,
    OrderDate   DATETIME2 (0)   NOT NULL,
    Status      VARCHAR (20)    NOT NULL,
    Region      VARCHAR (20)    NOT NULL,
    Amount      DECIMAL (10, 2) NOT NULL,
    Notes       VARCHAR (400)   NOT NULL
);
GO

PRINT '=== 1. TABLE CREATED (heap, no indexes) ===';
GO


--------------------------------------------------------------------------
-- 2. Data generation (~100,000 rows)
--------------------------------------------------------------------------
-- Classic set-based "row generator": a cascade of self cross-joins builds
-- a numbers sequence far larger than 100,000 without needing a physical
-- tally table, recursion, or a cross-database reference to a system
-- table (not usable in Azure SQL Database anyway).
--
-- Column shapes, chosen so each index has a genuine, distinct purpose:
--   CustomerId : 1..5000            -> ~20 orders/customer  (good seek target)
--   OrderDate  : spread over ~2 yrs -> supports range scans (clustering key)
--   Status     : 6 values           -> low cardinality alone, useful when
--                                       combined with a date range
--   Region     : 5 values           -> not indexed; realistic "extra" column
--   Amount     : random money value -> realistic payload / included column
--   Notes      : ~120-400 char text -> realistic payload, pads row size so
--                                       a full scan is not trivially 1-2 pages
INSERT INTO dbo.OrderActivity (CustomerId, OrderDate, Status, Region, Amount, Notes)
SELECT
    CustomerId  = 1 + (rg.N % 5000),
    OrderDate   = DATEADD(SECOND, ABS(CHECKSUM(NEWID())) % (730 * 86400), '2024-08-19'),
    Status      = CASE rg.N % 6
                      WHEN 0 THEN 'Pending'
                      WHEN 1 THEN 'Processing'
                      WHEN 2 THEN 'Shipped'
                      WHEN 3 THEN 'Delivered'
                      WHEN 4 THEN 'Cancelled'
                      ELSE 'Returned'
                  END,
    Region      = CASE (rg.N / 6) % 5
                      WHEN 0 THEN 'North'
                      WHEN 1 THEN 'South'
                      WHEN 2 THEN 'East'
                      WHEN 3 THEN 'West'
                      ELSE 'Central'
                  END,
    Amount      = CAST(9.99 + (ABS(CHECKSUM(NEWID())) % 100000) / 100.0 AS DECIMAL(10, 2)),
    Notes       = CASE rg.N % 8
                      WHEN 0 THEN 'Customer requested expedited handling.'
                      WHEN 1 THEN 'Packaging was double-checked before dispatch.'
                      WHEN 2 THEN 'Address was verified against the last known shipment.'
                      WHEN 3 THEN 'Discount code applied at checkout.'
                      WHEN 4 THEN 'Follow-up call scheduled for delivery confirmation.'
                      WHEN 5 THEN 'Item flagged for quality check prior to shipping.'
                      WHEN 6 THEN 'Customer opted into the loyalty program at checkout.'
                      ELSE 'Backorder resolved after supplier restock.'
                  END
                  + ' ' +
                  CASE (rg.N / 8) % 8
                      WHEN 0 THEN 'Warehouse confirmed stock before release.'
                      WHEN 1 THEN 'Invoice was emailed to the account on file.'
                      WHEN 2 THEN 'Return window noted in the confirmation email.'
                      WHEN 3 THEN 'Gift wrap requested for this shipment.'
                      WHEN 4 THEN 'Partial shipment expected due to split inventory.'
                      WHEN 5 THEN 'Priority lane used for regional carrier pickup.'
                      WHEN 6 THEN 'Payment authorization retried once before success.'
                      ELSE 'No exceptions logged for this order.'
                  END
                  + ' ' +
                  CASE (rg.N / 64) % 8
                      WHEN 0 THEN 'Support ticket closed with no follow-up needed.'
                      WHEN 1 THEN 'Customer confirmed address by reply email.'
                      WHEN 2 THEN 'Bundled with a promotional insert.'
                      WHEN 3 THEN 'Carrier tracking number generated at handoff.'
                      WHEN 4 THEN 'Weight and dimensions recorded for freight class.'
                      WHEN 5 THEN 'Fraud check passed automatically.'
                      WHEN 6 THEN 'Manual review cleared by operations.'
                      ELSE 'Standard processing, nothing unusual to note.'
                  END
FROM
(
    SELECT TOP (100000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS N
    FROM
    (
        SELECT 1 AS c UNION ALL SELECT 1
    ) AS L0 (c)
    CROSS JOIN (SELECT 1 AS c UNION ALL SELECT 1) AS L1 (c)
    CROSS JOIN (SELECT 1 AS c UNION ALL SELECT 1) AS L2 (c)
    CROSS JOIN (SELECT 1 AS c UNION ALL SELECT 1) AS L3 (c)
    CROSS JOIN (SELECT 1 AS c UNION ALL SELECT 1) AS L4 (c)
    CROSS JOIN (SELECT 1 AS c UNION ALL SELECT 1) AS L5 (c)
    CROSS JOIN (SELECT 1 AS c UNION ALL SELECT 1) AS L6 (c)
    CROSS JOIN (SELECT 1 AS c UNION ALL SELECT 1) AS L7 (c)
    CROSS JOIN (SELECT 1 AS c UNION ALL SELECT 1) AS L8 (c)
    CROSS JOIN (SELECT 1 AS c UNION ALL SELECT 1) AS L9 (c)
    CROSS JOIN (SELECT 1 AS c UNION ALL SELECT 1) AS L10 (c)
    CROSS JOIN (SELECT 1 AS c UNION ALL SELECT 1) AS L11 (c)
    CROSS JOIN (SELECT 1 AS c UNION ALL SELECT 1) AS L12 (c)
    CROSS JOIN (SELECT 1 AS c UNION ALL SELECT 1) AS L13 (c)
    CROSS JOIN (SELECT 1 AS c UNION ALL SELECT 1) AS L14 (c)
    CROSS JOIN (SELECT 1 AS c UNION ALL SELECT 1) AS L15 (c)
    CROSS JOIN (SELECT 1 AS c UNION ALL SELECT 1) AS L16 (c)
) AS rg (N);
GO

PRINT '=== 2. DATA GENERATED ===';
SELECT TotalRows = COUNT(*), DistinctCustomers = COUNT(DISTINCT CustomerId) FROM dbo.OrderActivity;
GO

-- Write-side cost, "before" measurement: insert a small batch into the
-- HEAP (zero indexes to maintain yet). SET STATISTICS TIME ON reports
-- elapsed/CPU time in the message stream alongside STATISTICS IO reads.
SET STATISTICS TIME ON;
SET STATISTICS IO ON;

INSERT INTO dbo.OrderActivity (CustomerId, OrderDate, Status, Region, Amount, Notes)
SELECT
    1 + (n % 5000),
    DATEADD(SECOND, n, '2026-08-18'),
    'Pending',
    'North',
    99.99,
    'Write-cost benchmark row, heap phase, no indexes to maintain yet.'
FROM (SELECT TOP (2000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
      FROM sys.all_columns a CROSS JOIN sys.all_columns b) AS x;

SET STATISTICS TIME OFF;
GO

PRINT '=== 2b. WRITE-COST BASELINE INSERT DONE (heap, 0 indexes) — see STATISTICS TIME/IO above ===';
GO


--------------------------------------------------------------------------
-- 3. Baseline query (BEFORE any index exists)
--------------------------------------------------------------------------
-- Same table, three different predicates that will each be paired with
-- one of the three indexes created later. On a heap, every one of these
-- must do a full table scan — there is no index to seek into — so all
-- three baseline reads should land in the same ballpark (the size of the
-- table), regardless of predicate.

PRINT '=== 3. BASELINE QUERIES (heap, no indexes) ===';

-- 3a. Baseline for the clustered-index query (date range: last 31 days)
SET STATISTICS XML ON;
SELECT OrderCount = COUNT(*), TotalAmount = SUM(Amount)
FROM dbo.OrderActivity
WHERE OrderDate >= '2026-07-19' AND OrderDate < '2026-08-19';
SET STATISTICS XML OFF;
GO

-- 3b. Baseline for non-clustered index #1 (single customer lookup)
SET STATISTICS XML ON;
SELECT OrderCount = COUNT(*), TotalAmount = SUM(Amount)
FROM dbo.OrderActivity
WHERE CustomerId = 2500;
SET STATISTICS XML OFF;
GO

-- 3c. Baseline for non-clustered index #2 (status + date filter)
SET STATISTICS XML ON;
SELECT OrderCount = COUNT(*), TotalAmount = SUM(Amount)
FROM dbo.OrderActivity
WHERE Status = 'Pending' AND OrderDate < '2026-07-01';
SET STATISTICS XML OFF;
GO

PRINT '=== 3d. BASELINE STATISTICS IO CAPTURED ABOVE (see "Table ''OrderActivity''..." messages) ===';
GO


--------------------------------------------------------------------------
-- 4. Clustered index creation
--------------------------------------------------------------------------
-- OrderDate is the physical clustering key: it converts the heap into a
-- B-tree ordered by date, which is exactly what a date-range report
-- query (3a) needs to turn a full scan into a much narrower range scan.
CREATE CLUSTERED INDEX CIX_OrderActivity_OrderDate
    ON dbo.OrderActivity (OrderDate);
GO

PRINT '=== 4. CLUSTERED INDEX CREATED: CIX_OrderActivity_OrderDate (OrderDate) ===';
GO


--------------------------------------------------------------------------
-- 5. Clustered-index query (AFTER clustered index)
--------------------------------------------------------------------------
-- Same date-range predicate as 3a. Expect an Index Seek/Range Scan on
-- CIX_OrderActivity_OrderDate instead of a Table Scan, with logical reads
-- roughly proportional to (31 days / ~730 days) of the table instead of
-- the whole table.
SET STATISTICS XML ON;
SELECT OrderCount = COUNT(*), TotalAmount = SUM(Amount)
FROM dbo.OrderActivity
WHERE OrderDate >= '2026-07-19' AND OrderDate < '2026-08-19';
SET STATISTICS XML OFF;
GO

PRINT '=== 5. CLUSTERED-INDEX QUERY EXECUTED — see STATISTICS IO/plan above ===';
GO


--------------------------------------------------------------------------
-- 6. First non-clustered index creation
--------------------------------------------------------------------------
-- Supports the "all orders for one customer" lookup (3b). CustomerId is
-- highly selective (~20 rows out of 100,000 per value). Amount is
-- INCLUDEd so the query in step 7 is fully covered by the index — no
-- key/RID lookup back into the clustered index is needed.
CREATE NONCLUSTERED INDEX IX_OrderActivity_CustomerId
    ON dbo.OrderActivity (CustomerId)
    INCLUDE (Amount);
GO

PRINT '=== 6. NON-CLUSTERED INDEX #1 CREATED: IX_OrderActivity_CustomerId (CustomerId) INCLUDE (Amount) ===';
GO


--------------------------------------------------------------------------
-- 7. First non-clustered-index query (AFTER IX_OrderActivity_CustomerId)
--------------------------------------------------------------------------
-- Same predicate as 3b. Expect an Index Seek on IX_OrderActivity_CustomerId
-- with logical reads close to the depth of the B-tree plus the ~20
-- matching rows (i.e. a handful of pages, not the whole table).
SET STATISTICS XML ON;
SELECT OrderCount = COUNT(*), TotalAmount = SUM(Amount)
FROM dbo.OrderActivity
WHERE CustomerId = 2500;
SET STATISTICS XML OFF;
GO

PRINT '=== 7. NON-CLUSTERED-INDEX #1 QUERY EXECUTED — see STATISTICS IO/plan above ===';
GO


--------------------------------------------------------------------------
-- 8. Second non-clustered index creation
--------------------------------------------------------------------------
-- Supports the "orders in a given status older than a cutoff date" query
-- (3c) — a typical operations/queue-processing filter. Status is put
-- first (equality predicate), OrderDate second (range predicate) — the
-- standard "equality columns before range columns" composite-index rule.
-- Amount is INCLUDEd so this query is also fully covered.
CREATE NONCLUSTERED INDEX IX_OrderActivity_Status_OrderDate
    ON dbo.OrderActivity (Status, OrderDate)
    INCLUDE (Amount);
GO

PRINT '=== 8. NON-CLUSTERED INDEX #2 CREATED: IX_OrderActivity_Status_OrderDate (Status, OrderDate) INCLUDE (Amount) ===';
GO


--------------------------------------------------------------------------
-- 9. Second non-clustered-index query (AFTER IX_OrderActivity_Status_OrderDate)
--------------------------------------------------------------------------
-- Same predicate as 3c. Expect an Index Seek on
-- IX_OrderActivity_Status_OrderDate: equality seek on Status = 'Pending',
-- then a range scan on OrderDate within that Status.
SET STATISTICS XML ON;
SELECT OrderCount = COUNT(*), TotalAmount = SUM(Amount)
FROM dbo.OrderActivity
WHERE Status = 'Pending' AND OrderDate < '2026-07-01';
SET STATISTICS XML OFF;
GO

PRINT '=== 9. NON-CLUSTERED-INDEX #2 QUERY EXECUTED — see STATISTICS IO/plan above ===';
GO


--------------------------------------------------------------------------
-- 10. Write-side cost, "after" measurement
--------------------------------------------------------------------------
-- Same shape/size batch as the step-2 baseline insert, now that the
-- table carries 1 clustered index + 2 non-clustered indexes. Every row
-- now has to be placed in clustered-index order AND maintain both
-- non-clustered B-trees, so this is a fair, real comparison of write
-- cost against the step-2 numbers, from the same STATISTICS TIME/IO
-- mechanism.
SET STATISTICS TIME ON;
SET STATISTICS IO ON;

INSERT INTO dbo.OrderActivity (CustomerId, OrderDate, Status, Region, Amount, Notes)
SELECT
    1 + (n % 5000),
    DATEADD(SECOND, n, '2026-08-18'),
    'Pending',
    'North',
    99.99,
    'Write-cost benchmark row, post-index phase, 3 indexes to maintain now.'
FROM (SELECT TOP (2000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
      FROM sys.all_columns a CROSS JOIN sys.all_columns b) AS x;

SET STATISTICS TIME OFF;
SET STATISTICS IO OFF;
GO

PRINT '=== 10. WRITE-COST AFTER-INDEXES INSERT DONE (1 clustered + 2 non-clustered) — see STATISTICS TIME/IO above ===';
GO


--------------------------------------------------------------------------
-- 11. Final validation
--------------------------------------------------------------------------
-- Re-run all three targeted queries back-to-back with the actual
-- execution plan on, to confirm — from the plan itself, not from
-- assumption — that each query is now using the index built for it, and
-- to have one clean, final before/after comparison point.

PRINT '=== 11. FINAL VALIDATION: row/index inventory ===';

SELECT TotalRows = COUNT(*) FROM dbo.OrderActivity;

SELECT
    IndexName    = i.name,
    IndexType    = i.type_desc,
    KeyColumns   = keycols.KeyColumns,
    IncludedCols = inccols.IncludedCols
FROM sys.indexes AS i
OUTER APPLY
(
    SELECT KeyColumns = STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal)
    FROM sys.index_columns AS ic
    JOIN sys.columns AS c
        ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
) AS keycols
OUTER APPLY
(
    SELECT IncludedCols = STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY c.name)
    FROM sys.index_columns AS ic
    JOIN sys.columns AS c
        ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1
) AS inccols
WHERE i.object_id = OBJECT_ID(N'dbo.OrderActivity')
  AND i.type > 0
ORDER BY i.type_desc DESC, i.name;
GO

PRINT '=== 11b. FINAL VALIDATION: re-run all 3 queries with actual plan + STATISTICS IO ===';

SET STATISTICS IO ON;

SET STATISTICS XML ON;
SELECT OrderCount = COUNT(*), TotalAmount = SUM(Amount)
FROM dbo.OrderActivity
WHERE OrderDate >= '2026-07-19' AND OrderDate < '2026-08-19';
SET STATISTICS XML OFF;
GO

SET STATISTICS XML ON;
SELECT OrderCount = COUNT(*), TotalAmount = SUM(Amount)
FROM dbo.OrderActivity
WHERE CustomerId = 2500;
SET STATISTICS XML OFF;
GO

SET STATISTICS XML ON;
SELECT OrderCount = COUNT(*), TotalAmount = SUM(Amount)
FROM dbo.OrderActivity
WHERE Status = 'Pending' AND OrderDate < '2026-07-01';
SET STATISTICS XML OFF;

SET STATISTICS IO OFF;
GO

PRINT '=== DONE ===';
GO


--------------------------------------------------------------------------
-- Real, measured results (from actually running this script against
-- thinkschool-day7 on Azure SQL Database — nothing below is invented)
--------------------------------------------------------------------------
-- Rows generated (step 2): 100,000 rows, 5,000 distinct CustomerId values
-- (~20 orders/customer). Plus 2,000 rows from the step-2b write-cost
-- baseline insert (heap) and 2,000 rows from the step-10 write-cost
-- insert (after all 3 indexes) => 104,000 rows at final validation
-- (step 11 confirmed TotalRows = 104000 directly from the table).
--
-- Indexes confirmed present at final validation (queried from
-- sys.indexes/sys.index_columns, step 11):
--   CIX_OrderActivity_OrderDate         CLUSTERED     key=(OrderDate)
--   IX_OrderActivity_CustomerId         NONCLUSTERED  key=(CustomerId)          include=(Amount)
--   IX_OrderActivity_Status_OrderDate   NONCLUSTERED  key=(Status, OrderDate)   include=(Amount)
--
-- Logical reads on dbo.OrderActivity, per query, before/after its index
-- (table had 102,000 rows for the baseline+clustered+NC1+NC2 measurements
-- below, since the step-2b write-cost insert had already run; the actual
-- plan for every "after" run was inspected and confirms the expected
-- operator/index, not assumed):
--
--   Query                          | Reads BEFORE (heap, Table Scan) | Reads AFTER          | Actual plan operator (AFTER)
--   ------------------------------ | -------------------------------- | -------------------- | -----------------------------------------------------
--   3a/5  OrderDate range (31 days) | 2533 (6220 rows matched)         | 132 (6220 rows)       | Clustered Index Seek on CIX_OrderActivity_OrderDate
--   3b/7  CustomerId = 2500         | 2533 (20 rows matched)           | 2   (20 rows)         | Index Seek on IX_OrderActivity_CustomerId
--   3c/9  Status='Pending' + date   | 2533 (15585 rows matched)        | 65  (15585 rows)      | Index Seek on IX_OrderActivity_Status_OrderDate
--
-- Note all three BEFORE reads are identical (2533): a heap has no index
-- to seek into, so SQL Server must scan every page regardless of the
-- predicate — this is itself a real, observed property of heaps, not a
-- coincidence.
--
-- Final validation re-run (step 11b, table now at 104,000 rows, all 3
-- indexes present) reproduced the same operators with reads of 221 / 3 / 65
-- respectively; the row counts shifted by exactly the amount explainable
-- by the 2,000 step-10 rows (all dated 2026-08-18, Status='Pending',
-- CustomerId spread 1..5000): the date-range query picked up all 2,000
-- extra rows (6220 -> 8220, i.e. +2000, since those rows fall inside the
-- last-31-days window), the CustomerId=2500 query was unaffected (still
-- 20, as expected for one customer out of 5000), and the
-- Status+OrderDate<'2026-07-01' query was unaffected (still 15585, since
-- 2026-08-18 is not before the 2026-07-01 cutoff) — a genuine correctness
-- check that the indexes return the same rows as the heap did, not just
-- fewer reads.
--
-- Write-side cost (step 2b vs step 10, identical 2,000-row INSERT shape,
-- measured with SET STATISTICS TIME/IO ON):
--   Heap only (0 indexes)                : elapsed 115 ms, CPU 16 ms,  dbo.OrderActivity logical reads 2032
--   1 clustered + 2 non-clustered indexes: elapsed 3982 ms, CPU 109 ms, dbo.OrderActivity logical reads 18891
--   => ~35x slower elapsed time and ~9.3x more logical I/O to insert the
--      exact same 2,000 rows once 3 indexes have to be maintained instead
--      of 0 — the write-side tax the exercise asks about, measured, not
--      assumed.
--------------------------------------------------------------------------
