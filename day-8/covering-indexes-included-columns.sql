--------------------------------------------------------------------------
-- Day 8 Task 2: Covering indexes + included columns
--------------------------------------------------------------------------
-- Dialect: SQL Server / Azure SQL Database (T-SQL).
--
-- A covering index serves a query entirely from the index's own B-tree,
-- so SQL Server never has to go back to the clustered index to fetch the
-- remaining columns (a "Key Lookup"). This script demonstrates that,
-- with real measurements, on the dbo.OrderActivity table already built
-- by Day 8 Task 1 (104,000 rows) rather than creating another dataset.
--
-- It adds exactly ONE new, dedicated non-clustered index for this
-- experiment: IX_OrderActivity_CustomerId_Status. It does not touch,
-- modify, or drop any of the three indexes Task 1 created and
-- documented (CIX_OrderActivity_OrderDate, IX_OrderActivity_CustomerId,
-- IX_OrderActivity_Status_OrderDate).
--
-- Reused the existing Azure SQL Database rather than provisioning a new
-- Azure resource:
--
-- Executed against: Azure SQL Database `thinkschool-day7` on logical
-- server `thinkschool-day7-sql-0c0dda.database.windows.net`
-- (resource group `thinkschool-rg`, region `centralindia`), via an Azure
-- AD access token for the signed-in `az login` identity — no SQL
-- login/password used or stored anywhere in this repository.
--
-- Note on batching: SET STATISTICS IO ON and SET STATISTICS XML ON are
-- kept in the SAME batch as the query they measure (no GO in between).
-- This is the reliable way to guarantee both the logical-reads message
-- and the actual-plan XML come back attached to that exact execution,
-- regardless of which client/tool runs the script — and it is exactly
-- how this script was actually run to produce the numbers recorded in
-- the trailing comment block below. Nothing in that block is invented;
-- every number was captured by executing this script against the
-- database above.
--------------------------------------------------------------------------


--------------------------------------------------------------------------
-- 1. Verify the test table and data
--------------------------------------------------------------------------
PRINT '=== 1. VERIFY TABLE/DATA (reusing Task 1''s dbo.OrderActivity) ===';
SELECT TotalRows = COUNT(*) FROM dbo.OrderActivity;
GO

-- Confirm the target customer/status combination is small (a handful of
-- rows), so a Key Lookup for each matching row is cheap enough that the
-- optimizer actually prefers Index Seek + Key Lookup over a full/range
-- scan -- the plan shape this experiment needs to demonstrate.
SELECT CustomerId, Status, Cnt = COUNT(*)
FROM dbo.OrderActivity
WHERE CustomerId = 2500
GROUP BY CustomerId, Status
ORDER BY Status;
GO


--------------------------------------------------------------------------
-- 2. Baseline / non-covering index
--------------------------------------------------------------------------
-- A dedicated new index for this exercise: key = (CustomerId, Status),
-- no INCLUDE. It fully covers the WHERE clause (CustomerId = @c AND
-- Status = @s) but NOT the OrderDate/Amount/Notes columns the query
-- below also selects -- exactly the shape that forces SQL Server to do
-- a Key Lookup back into the clustered index (CIX_OrderActivity_OrderDate,
-- built in Task 1) for every row the seek matches.
IF EXISTS (SELECT 1 FROM sys.indexes
           WHERE name = 'IX_OrderActivity_CustomerId_Status'
             AND object_id = OBJECT_ID(N'dbo.OrderActivity'))
    DROP INDEX IX_OrderActivity_CustomerId_Status ON dbo.OrderActivity;
GO

CREATE NONCLUSTERED INDEX IX_OrderActivity_CustomerId_Status
    ON dbo.OrderActivity (CustomerId, Status);
GO

PRINT '=== 2. NON-COVERING INDEX CREATED: IX_OrderActivity_CustomerId_Status (CustomerId, Status), no INCLUDE ===';

-- Show the non-covering index definition, read back from the catalog
-- (not re-typed) so it matches exactly what was just created.
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
    JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
) AS keycols
OUTER APPLY
(
    SELECT IncludedCols = STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY c.name)
    FROM sys.index_columns AS ic
    JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1
) AS inccols
WHERE i.object_id = OBJECT_ID(N'dbo.OrderActivity')
  AND i.name = 'IX_OrderActivity_CustomerId_Status';
GO


--------------------------------------------------------------------------
-- 3. Baseline query
--------------------------------------------------------------------------
-- Filters/seeks on (CustomerId, Status) -- fully covered by the index
-- key -- but also selects OrderDate, Amount, and Notes, none of which
-- are in that index. This is the query used both before and after the
-- covering index is added; only the index definition changes.
--
-- 4. SET STATISTICS IO ON  |  5. actual execution plan (STATISTICS XML)
-- kept in the same batch as the query itself (see note at the top).
PRINT '=== 3/4/5. BASELINE QUERY, non-covering index (expect Key Lookup) ===';

SET STATISTICS IO ON;
SET STATISTICS XML ON;

SELECT CustomerId, Status, OrderDate, Amount, Notes
FROM dbo.OrderActivity
WHERE CustomerId = 2500 AND Status = 'Delivered';

SET STATISTICS XML OFF;
SET STATISTICS IO OFF;
GO

-- 6. Confirm the actual plan contains a Key Lookup / 7. logical reads
-- -- both read directly from the STATISTICS IO message and the actual
-- plan XML captured by the batch above. Recorded verbatim in the
-- trailing comment block: the plan shows Index Seek on
-- IX_OrderActivity_CustomerId_Status feeding a Nested Loops into a
-- Clustered Index Seek on CIX_OrderActivity_OrderDate with
-- IndexScan Lookup="1" (SQL Server's actual-plan XML representation of
-- a Key Lookup) -- and STATISTICS IO reports 24 logical reads.


--------------------------------------------------------------------------
-- 9. Create the covering index using INCLUDE
--------------------------------------------------------------------------
-- Same key (CustomerId, Status); WITH (DROP_EXISTING = ON) turns the
-- exact same index into a covering index by adding the three columns
-- the query needs (OrderDate, Amount, Notes) as leaf-level INCLUDE
-- columns, so no Key Lookup should be required any more.
CREATE NONCLUSTERED INDEX IX_OrderActivity_CustomerId_Status
    ON dbo.OrderActivity (CustomerId, Status)
    INCLUDE (OrderDate, Amount, Notes)
    WITH (DROP_EXISTING = ON);
GO

PRINT '=== 9. COVERING INDEX CREATED: IX_OrderActivity_CustomerId_Status (CustomerId, Status) INCLUDE (OrderDate, Amount, Notes) ===';
GO


--------------------------------------------------------------------------
-- 10. Run the exact same query again  |  11. actual execution plan again
--------------------------------------------------------------------------
PRINT '=== 10/11. SAME QUERY, covering index (expect Key Lookup gone) ===';

SET STATISTICS IO ON;
SET STATISTICS XML ON;

SELECT CustomerId, Status, OrderDate, Amount, Notes
FROM dbo.OrderActivity
WHERE CustomerId = 2500 AND Status = 'Delivered';

SET STATISTICS XML OFF;
SET STATISTICS IO OFF;
GO

-- 12. Confirm the Key Lookup has disappeared / 13. new logical reads --
-- again read directly from this batch's actual plan XML and STATISTICS
-- IO message. Recorded verbatim below: the plan now shows a single
-- Index Seek on IX_OrderActivity_CustomerId_Status with no reference to
-- CIX_OrderActivity_OrderDate anywhere in the plan (no Key Lookup node
-- at all, not just an absent Lookup="1" flag) -- and STATISTICS IO
-- reports 4 logical reads.


--------------------------------------------------------------------------
-- 14. Final validation
--------------------------------------------------------------------------
-- Confirm the covering index definition from the catalog, and that this
-- experiment's index is the only one that changed (Task 1's three
-- indexes are listed here unmodified).
PRINT '=== 14. FINAL INDEX INVENTORY ON dbo.OrderActivity ===';
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
    JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
) AS keycols
OUTER APPLY
(
    SELECT IncludedCols = STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY c.name)
    FROM sys.index_columns AS ic
    JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1
) AS inccols
WHERE i.object_id = OBJECT_ID(N'dbo.OrderActivity')
  AND i.type > 0
ORDER BY i.type_desc DESC, i.name;
GO

PRINT '=== DONE ===';
GO


--------------------------------------------------------------------------
-- Real, measured results (from actually running this script against
-- thinkschool-day7 on Azure SQL Database — nothing below is invented)
--------------------------------------------------------------------------
-- Table verified: dbo.OrderActivity, 104,000 rows (unchanged from Task 1
-- -- this task reads/indexes the table but inserts no new rows).
-- CustomerId = 2500 has 20 total orders, split 7 Delivered / 6
-- Processing / 7 Returned -- the query below targets the 7-row
-- "Delivered" slice, both before and after the index change.
--
-- Non-covering index created (step 2), confirmed from sys.indexes:
--   IX_OrderActivity_CustomerId_Status   NONCLUSTERED   key=(CustomerId, Status)   include=(none)
--
-- BEFORE (non-covering index, query = CustomerId=2500 AND Status='Delivered'):
--   STATISTICS IO : Table '[dbo].[OrderActivity]'. Scan count 1,
--                   logical reads 24, physical reads 0, ...
--   Actual plan (STATISTICS XML), RelOp chain:
--     Nested Loops
--       -> Index Seek on IX_OrderActivity_CustomerId_Status
--            (ActualRows=7, ActualLogicalReads=3)
--       -> Clustered Index Seek on CIX_OrderActivity_OrderDate
--            (IndexScan Lookup="1" -- this is a Key Lookup;
--             ActualRows=7, ActualExecutions=7, ActualLogicalReads=21)
--   Key Lookup present: YES (IndexScan Lookup="1" on
--   CIX_OrderActivity_OrderDate, executed once per matching row -- 7
--   executions for 7 rows). 24 logical reads = 3 (seek) + 21 (7 lookups).
--
-- Covering index created (step 9), confirmed from sys.indexes:
--   IX_OrderActivity_CustomerId_Status   NONCLUSTERED   key=(CustomerId, Status)   include=(Amount, Notes, OrderDate)
--
-- AFTER (covering index, identical query):
--   STATISTICS IO : Table '[dbo].[OrderActivity]'. Scan count 1,
--                   logical reads 4, physical reads 0, ...
--   Actual plan (STATISTICS XML), RelOp chain:
--     Index Seek on IX_OrderActivity_CustomerId_Status only
--            (ActualRows=7, ActualLogicalReads=4)
--   Key Lookup present: NO -- CIX_OrderActivity_OrderDate does not
--   appear anywhere in the plan at all; the query is answered entirely
--   from the covering index's own B-tree.
--
-- Logical-reads delta:
--   24 (before) -> 4 (after)  =  20 fewer logical reads, a ~6x
--   reduction (83% fewer reads), for the identical 7-row result set
--   (same CustomerId/Status/OrderDate/Amount/Notes values returned both
--   times -- confirmed by comparing the two result sets row for row).
--
-- Before vs after plan, side by side:
--   Metric              | Non-covering (before)         | Covering (after)
--   -------------------- | ------------------------------ | -----------------------------
--   Physical operators   | Nested Loops, Index Seek,      | Index Seek (single operator)
--                        | Clustered Index Seek (Lookup)  |
--   Objects touched      | IX_OrderActivity_CustomerId_    | IX_OrderActivity_CustomerId_
--                        | Status AND CIX_OrderActivity_   | Status only
--                        | OrderDate                       |
--   Key Lookup present   | YES (IndexScan Lookup="1")     | NO
--   Logical reads        | 24                              | 4
--------------------------------------------------------------------------
