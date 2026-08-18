# Day 8 — Clustered vs non-clustered indexes

> "Indexes are the single biggest lever on read performance — and a tax
> on writes. Create a clustered index and two non-clustered indexes on a
> table with ~100k rows (generate the data). Use SET STATISTICS IO ON
> and the actual execution plan."

The full, runnable script lives at [`Task 1.sql`](./Task%201.sql) in this
same folder (schema, data generation, every query below, and inline
comments). This README is the write-up of what that script contains and
the actual results of running it against Azure SQL. Nothing in this
document is invented — every row count, logical-read number, and
execution-plan operator below was captured by actually executing the
script and reading its output.

## Objective

This exercise demonstrates, with real measurements rather than theory,
how far an index can move read cost, and that the move isn't free: a
clustered index and two non-clustered indexes are added one at a time to
a ~100,000-row table, with `SET STATISTICS IO` and the actual execution
plan captured before and after each one, so the improvement (or lack of
it) is verified from the plan itself instead of assumed. A small
write-cost experiment then measures the other side of that trade — what
maintaining those same three indexes costs a plain `INSERT`.

## Azure SQL Database

- **Database:** `thinkschool-day7`
- **Resource group:** `thinkschool-rg`
- **Region:** Central India (`centralindia`)

Reused the existing Day 7 Azure SQL Database rather than provisioning a
new Azure resource. Connected using an Azure AD access token for the
signed-in `az login` identity — no SQL login, password, or connection
string with credentials is stored anywhere in this repository.

## Dataset

- **Table:** `dbo.OrderActivity`, created as a **heap** (no primary key
  or clustered index) so the baseline measurement reflects a genuine
  no-index state, not an accidental default index.
- **Rows:** 100,000 generated rows (confirmed by `COUNT(*)` at the point
  of generation: `TotalRows = 100000`, `DistinctCustomers = 5000`). Two
  further 2,000-row batches are inserted later purely to measure
  write-side cost (see [§8](#8-write-side-cost)), bringing the table to
  104,000 rows by the end of the script.
- **Important columns:**
  - `CustomerId` — 1..5000, ~20 orders per customer (selective lookup
    target for non-clustered index #1).
  - `OrderDate` — spread over ~2 years (clustering key; supports
    date-range scans).
  - `Status` — 6 values (`Pending`, `Processing`, `Shipped`,
    `Delivered`, `Cancelled`, `Returned`); low cardinality alone, useful
    combined with a date range (non-clustered index #2).
  - `Amount`, `Region`, `Notes` — realistic payload columns (synthetic
    text/values only, no real people or PII); `Notes` is padded to
    ~120–400 characters so a full table scan isn't trivially 1–2 pages.
- **Data generation:** a classic set-based "row generator" — a cascade
  of self cross-joins on two-row derived tables builds a numbers
  sequence far larger than 100,000, with no physical tally table,
  recursion, or cross-database reference needed (the latter isn't
  available in Azure SQL Database anyway). `CustomerId`/`Status`/`Region`
  are derived deterministically from the row number via modulo
  arithmetic; `OrderDate` and `Amount` use `NEWID()`-seeded randomness
  for a realistic, non-uniform spread.

## Exercise

> "Paste the index DDL, a query that uses each, and the logical-reads
> before/after each index. One line on the write-side cost you
> observed."

## 1. Baseline — Before Indexes

Query (same predicate shape run three ways, once per index this baseline
will be compared against):

```sql
SELECT OrderCount = COUNT(*), TotalAmount = SUM(Amount)
FROM dbo.OrderActivity
WHERE OrderDate >= '2026-07-19' AND OrderDate < '2026-08-19';   -- date range

SELECT OrderCount = COUNT(*), TotalAmount = SUM(Amount)
FROM dbo.OrderActivity
WHERE CustomerId = 2500;                                         -- single customer

SELECT OrderCount = COUNT(*), TotalAmount = SUM(Amount)
FROM dbo.OrderActivity
WHERE Status = 'Pending' AND OrderDate < '2026-07-01';           -- status + date
```

**STATISTICS IO output** (identical for all three, table at 102,000 rows
at this point — the step-2b write-cost batch had already run):

```
Table '[dbo].[OrderActivity]'. Scan count 1, logical reads 2533, physical reads 0, ...
```

**Logical reads:** **2,533** for all three queries.

**Actual execution-plan observation:** all three plans show a
`Table Scan` on `[OrderActivity]` (`EstRows≈5758/19/17528`,
`ActualRows=6220/20/15585`). There is no index to seek into on a heap, so
every query — regardless of how selective its predicate is — pays the
same full-table-scan cost. This is the real, observed baseline, not an
assumption.

## 2. Clustered Index

```sql
CREATE CLUSTERED INDEX CIX_OrderActivity_OrderDate
    ON dbo.OrderActivity (OrderDate);
```

Query that uses it (same date-range query as the baseline):

```sql
SELECT OrderCount = COUNT(*), TotalAmount = SUM(Amount)
FROM dbo.OrderActivity
WHERE OrderDate >= '2026-07-19' AND OrderDate < '2026-08-19';
```

**Logical reads:** **132** (down from 2,533 baseline), same 6,220 rows
returned.

**Before/after:** 2,533 → 132 (≈19× fewer logical reads).

**Actual execution-plan observation:** the plan's `RelOp` list now shows
`Clustered Index Seek` on `[OrderActivity].[CIX_OrderActivity_OrderDate]`
(`EstRows=6216.37`, `ActualRows=6220`) feeding a `Nested Loops` /
`Merge Interval` shape that SQL Server generates for the two-boundary
date range — not a `Table Scan`. The index is confirmed used by the plan
itself.

## 3. Non-clustered Index #1

```sql
CREATE NONCLUSTERED INDEX IX_OrderActivity_CustomerId
    ON dbo.OrderActivity (CustomerId)
    INCLUDE (Amount);
```

Query that uses it:

```sql
SELECT OrderCount = COUNT(*), TotalAmount = SUM(Amount)
FROM dbo.OrderActivity
WHERE CustomerId = 2500;
```

**Logical reads:** **2** (down from 2,533 baseline), same 20 rows
returned.

**Before/after:** 2,533 → 2 (≈1,266× fewer logical reads).

**Actual execution-plan observation:** the plan shows a single
`Index Seek` on `[OrderActivity].[IX_OrderActivity_CustomerId]`
(`EstRows=20`, `ActualRows=20`) feeding directly into `Stream Aggregate`
— no key lookup back into the clustered index, because `Amount` is
covered by the `INCLUDE`.

## 4. Non-clustered Index #2

```sql
CREATE NONCLUSTERED INDEX IX_OrderActivity_Status_OrderDate
    ON dbo.OrderActivity (Status, OrderDate)
    INCLUDE (Amount);
```

Query that uses it:

```sql
SELECT OrderCount = COUNT(*), TotalAmount = SUM(Amount)
FROM dbo.OrderActivity
WHERE Status = 'Pending' AND OrderDate < '2026-07-01';
```

**Logical reads:** **65** (down from 2,533 baseline), same 15,585 rows
returned.

**Before/after:** 2,533 → 65 (≈39× fewer logical reads).

**Actual execution-plan observation:** the plan shows an `Index Seek` on
`[OrderActivity].[IX_OrderActivity_Status_OrderDate]`
(`EstRows=17851.5`, `ActualRows=15585`) — an equality seek on
`Status = 'Pending'` combined with a range scan on `OrderDate` within
that status, exactly the "equality columns before range columns"
composite-index pattern the index was built for.

## 5. Logical Reads Comparison

| Stage | Query | Logical Reads | Execution Plan |
|---|---|---:|---|
| Baseline | `OrderDate` range (31 days) | 2,533 | Table Scan |
| Clustered index | `OrderDate` range (31 days) | 132 | Clustered Index Seek on `CIX_OrderActivity_OrderDate` |
| Baseline | `CustomerId = 2500` | 2,533 | Table Scan |
| Non-clustered index #1 | `CustomerId = 2500` | 2 | Index Seek on `IX_OrderActivity_CustomerId` |
| Baseline | `Status='Pending'` + date | 2,533 | Table Scan |
| Non-clustered index #2 | `Status='Pending'` + date | 65 | Index Seek on `IX_OrderActivity_Status_OrderDate` |

Final validation re-run (table at 104,000 rows, all three indexes
present) reproduced the same operators with reads of **221 / 3 / 65**
respectively — the small increases over the 132/2/65 figures above are
explained exactly by the 2,000 extra rows inserted in between (see
[§7](#7-actual-execution-plan)), not by any change in which index was
used.

## 6. Index DDL

```sql
-- Clustered index
CREATE CLUSTERED INDEX CIX_OrderActivity_OrderDate
    ON dbo.OrderActivity (OrderDate);
```

```sql
-- Non-clustered index #1
CREATE NONCLUSTERED INDEX IX_OrderActivity_CustomerId
    ON dbo.OrderActivity (CustomerId)
    INCLUDE (Amount);
```

```sql
-- Non-clustered index #2
CREATE NONCLUSTERED INDEX IX_OrderActivity_Status_OrderDate
    ON dbo.OrderActivity (Status, OrderDate)
    INCLUDE (Amount);
```

Confirmed present at final validation directly from
`sys.indexes`/`sys.index_columns`:

| IndexName | IndexType | KeyColumns | IncludedCols |
|---|---|---|---|
| `IX_OrderActivity_CustomerId` | NONCLUSTERED | `CustomerId` | `Amount` |
| `IX_OrderActivity_Status_OrderDate` | NONCLUSTERED | `Status, OrderDate` | `Amount` |
| `CIX_OrderActivity_OrderDate` | CLUSTERED | `OrderDate` | *(none)* |

## 7. Actual Execution Plan

Every "after" query above was run with `SET STATISTICS XML ON`, and the
resulting actual-plan XML was parsed (not just glanced at) for its
`RelOp` operators, `PhysicalOp`, and the `Object`/`Index` each operator
touched — the summaries in §2–§4 are read directly from that XML, so an
index is only described as "used" where the plan itself names it.

The final validation pass (step 11b in the script) re-ran all three
queries once more against the fully-indexed, 104,000-row table and
produced the same three operators — `Clustered Index Seek` on
`CIX_OrderActivity_OrderDate`, `Index Seek` on
`IX_OrderActivity_CustomerId`, `Index Seek` on
`IX_OrderActivity_Status_OrderDate` — with logical reads of 221, 3, and
65. The row counts moved in a way that itself confirms correctness, not
just lower cost: the date-range query picked up exactly the 2,000 rows
added by the step-10 write-cost insert (6,220 → 8,220 — all of those
rows are dated `2026-08-18`, inside the 31-day window), while the
`CustomerId = 2500` query and the `Status/OrderDate` query were both
unaffected (still 20 and 15,585 rows), because those same 2,000 rows are
spread across all 5,000 customers and are dated after the
`OrderDate < '2026-07-01'` cutoff. The indexed queries return exactly
the rows the heap would have — they are just far cheaper to find.

## 8. Write-side Cost

Measured directly by running the identical 2,000-row `INSERT` twice —
once against the heap with zero indexes (step 2b), once against the same
table after all three indexes existed (step 10) — with
`SET STATISTICS TIME/IO ON` both times:

| | Elapsed | CPU | `dbo.OrderActivity` logical reads |
|---|---:|---:|---:|
| Heap (0 indexes) | 115 ms | 16 ms | 2,032 |
| 1 clustered + 2 non-clustered indexes | 3,982 ms | 109 ms | 18,891 |

**One line:** adding the clustered index and the two non-clustered
indexes made the exact same 2,000-row insert roughly **35× slower in
elapsed time and ~9.3× more logical I/O**, because every row now has to
be placed in clustered-key order and both non-clustered B-trees have to
be maintained alongside it — the write-side tax the exercise asks about,
measured rather than assumed.

## 9. Actual Validation

| Check | Result |
|---|---|
| Azure SQL execution | PASS |
| ~100,000 rows generated | PASS (100,000 exactly, confirmed via `COUNT(*)`) |
| Baseline query | PASS (executed for all 3 predicates, `Table Scan` confirmed) |
| Clustered index | PASS (created; confirmed in `sys.indexes` as `CLUSTERED` on `OrderDate`) |
| Non-clustered index #1 | PASS (created; confirmed in `sys.indexes` as `NONCLUSTERED` on `CustomerId`) |
| Non-clustered index #2 | PASS (created; confirmed in `sys.indexes` as `NONCLUSTERED` on `Status, OrderDate`) |
| `STATISTICS IO` | PASS (real `logical reads` captured for every query, before and after) |
| Actual execution plan | PASS (`SET STATISTICS XML ON`, parsed for `PhysicalOp`/`Object` per query) |
| Logical-read comparison | PASS (§5 table populated entirely from real captured numbers) |

## 10. Remaining Work

Day 8 Task 1 is complete; no remaining implementation work.

Two SQL bugs were hit and fixed during implementation (both visible only
once run against real Azure SQL, which is why they're noted here): using
`RowCount` as a column alias collides with the reserved `ROWCOUNT`
keyword (renamed to `TotalRows`), and combining two `STRING_AGG(...)
WITHIN GROUP` calls with different orderings in one `GROUP BY` scope is
rejected by SQL Server (error 8711; restructured with `OUTER APPLY`).
The version of `Task 1.sql` in this folder has both fixes and its final
run completed with zero errors.

---

# Day 8 — Covering indexes + included columns

> "A covering index serves a query entirely from the index, avoiding the
> key lookup. A query should initially perform a key lookup, then a
> covering index should be created using INCLUDE columns to eliminate
> the lookup."

The full, runnable script lives at
[`covering-indexes-included-columns.sql`](./covering-indexes-included-columns.sql)
in this same folder. This section is the write-up of what that script
contains and the actual results of running it against Azure SQL. Nothing
below is invented — the logical-reads numbers and the plan operators
were read directly from `SET STATISTICS IO` output and the actual
execution-plan XML (`SET STATISTICS XML ON`) captured while executing
the script.

## Objective

This exercise demonstrates, with a real before/after measurement rather
than theory, how a covering index eliminates a Key Lookup: a
non-clustered index is created whose key covers a query's `WHERE`
clause but not its `SELECT` list, the actual execution plan is captured
and confirmed to contain a Key Lookup, `INCLUDE` columns are then added
to the same index, and the identical query is re-run to confirm — from
the plan itself, not assumed — that the Key Lookup is gone and to
measure the resulting logical-reads delta.

## Azure SQL Database

Same database as Task 1 — reused, not re-provisioned:

- **Database:** `thinkschool-day7`
- **Resource group:** `thinkschool-rg`
- **Region:** Central India (`centralindia`)

Connected using an Azure AD access token for the signed-in `az login`
identity, same as Task 1 — no SQL login, password, or connection string
with credentials is stored anywhere in this repository.

## Exercise

> "Paste the before plan (showing the key lookup), the index with
> INCLUDE, the after plan (lookup gone), and the logical-reads delta."

## Dataset

- **Table:** `dbo.OrderActivity` — reused directly from Day 8 Task 1, no
  new table or data generation for this task.
- **Rows:** 104,000 (confirmed by `COUNT(*)` at the start of this
  script — unchanged from where Task 1 left the table; this task inserts
  no new rows).
- **Relevant columns:**
  - `CustomerId` — 1..5000, ~20 orders per customer.
  - `Status` — 6 values (`Pending`, `Processing`, `Shipped`,
    `Delivered`, `Cancelled`, `Returned`).
  - `OrderDate`, `Amount`, `Notes` — payload columns selected by the
    query below but deliberately left out of the non-covering index's
    key, so they must be fetched separately the first time.
- **Why this table/query is appropriate:** `CustomerId = 2500` has 20
  total orders, split 7 `Delivered` / 6 `Processing` / 7 `Returned`
  (confirmed by `GROUP BY` before the experiment). Filtering on
  `CustomerId = 2500 AND Status = 'Delivered'` targets a selective,
  7-row slice — small enough that the optimizer genuinely prefers an
  Index Seek + Key Lookup plan over a scan, which is what makes the
  "before" half of this experiment real rather than contrived. Using
  this existing table means the only new object needed is one
  dedicated index scoped to this experiment
  (`IX_OrderActivity_CustomerId_Status`); Task 1's three indexes
  (`CIX_OrderActivity_OrderDate`, `IX_OrderActivity_CustomerId`,
  `IX_OrderActivity_Status_OrderDate`) are never touched, confirmed
  unchanged in the final index inventory (§7 below).

## 1. Before — Non-Covering Index

**Index definition:**

```sql
CREATE NONCLUSTERED INDEX IX_OrderActivity_CustomerId_Status
    ON dbo.OrderActivity (CustomerId, Status);
```

**Query** (unchanged before and after):

```sql
SELECT CustomerId, Status, OrderDate, Amount, Notes
FROM dbo.OrderActivity
WHERE CustomerId = 2500 AND Status = 'Delivered';
```

**Why the index does not cover the query:** the index key is
`(CustomerId, Status)`, so it fully satisfies the `WHERE` clause, but
the `SELECT` list also asks for `OrderDate`, `Amount`, and `Notes` —
none of which are in the index (no `INCLUDE`). SQL Server can find the
right rows from the index alone but must go back to the table's
clustered index (`CIX_OrderActivity_OrderDate`) to fetch those three
columns for every matching row.

**Actual `STATISTICS IO` output:**

```
Table '[dbo].[OrderActivity]'. Scan count 1, logical reads 24, physical reads 0, ...
```

**Actual logical reads: 24.**

## 2. Before Execution Plan

Captured with `SET STATISTICS XML ON` in the same batch as the query
(so the actual, not estimated, plan is what's reported here). The
`RelOp`/`PhysicalOp` chain read directly from that plan XML:

```
Nested Loops
 ├─ Index Seek           on IX_OrderActivity_CustomerId_Status   (ActualRows=7, ActualLogicalReads=3)
 └─ Clustered Index Seek on CIX_OrderActivity_OrderDate          (ActualRows=7, ActualExecutions=7, ActualLogicalReads=21)
                                                                   IndexScan Lookup="1"
```

**The actual plan confirms a Key Lookup is present.** `Lookup="1"` on
the `Clustered Index Seek` operator is SQL Server's actual-plan XML
representation of a Key Lookup (SSMS renders this same operator as "Key
Lookup (Clustered)"). It ran once per matching row — `ActualExecutions
= 7` for the 7 rows returned — which is exactly why the read cost is
higher than the seek alone: `24 = 3 (seek into
IX_OrderActivity_CustomerId_Status) + 21 (7 separate single-row
lookups into CIX_OrderActivity_OrderDate)`. SQL Server had to perform
this lookup because `OrderDate`, `Amount`, and `Notes` exist only in
the clustered index's leaf rows, not in the non-covering index being
sought.

## 3. Covering Index with INCLUDE

Real `CREATE INDEX` statement used (same key and index name as §1;
`WITH (DROP_EXISTING = ON)` turns it into a covering index in place
rather than creating a second, redundant index):

```sql
CREATE NONCLUSTERED INDEX IX_OrderActivity_CustomerId_Status
ON dbo.OrderActivity
(
    CustomerId,
    Status
)
INCLUDE
(
    OrderDate,
    Amount,
    Notes
)
WITH (DROP_EXISTING = ON);
```

`OrderDate`, `Amount`, and `Notes` are exactly the three columns the
query in §1 needed beyond the key — nothing extra was included.

## 4. After — Same Query, Covering Index

Same query as §1, run again unchanged after the index in §3 was
created:

```sql
SELECT CustomerId, Status, OrderDate, Amount, Notes
FROM dbo.OrderActivity
WHERE CustomerId = 2500 AND Status = 'Delivered';
```

**Actual `STATISTICS IO` output:**

```
Table '[dbo].[OrderActivity]'. Scan count 1, logical reads 4, physical reads 0, ...
```

**Actual logical reads: 4.**

## 5. After Execution Plan

Captured the same way as §2 (`SET STATISTICS XML ON` in the same batch
as the query):

```
Index Seek on IX_OrderActivity_CustomerId_Status only   (ActualRows=7, ActualLogicalReads=4)
```

**The Key Lookup is gone.** `CIX_OrderActivity_OrderDate` does not
appear anywhere in this plan at all — not merely an absent `Lookup="1"`
flag, the clustered index is never referenced. The single `Index Seek`
on the now-covering `IX_OrderActivity_CustomerId_Status` returns all
five selected columns directly from its own B-tree. The same 7 rows
come back, with identical `OrderDate`/`Amount`/`Notes` values, confirmed
by comparing both result sets row for row.

## 6. Logical Reads Comparison

| | Before (non-covering) | After (covering) |
|---|---|---|
| Physical operators | Nested Loops → Index Seek → Clustered Index Seek | Index Seek (single operator) |
| Objects touched | `IX_OrderActivity_CustomerId_Status` **and** `CIX_OrderActivity_OrderDate` | `IX_OrderActivity_CustomerId_Status` only |
| Key Lookup present | **YES** (`IndexScan Lookup="1"`, 7 executions) | **NO** |
| Logical reads | **24** | **4** |

**Logical-reads delta: 24 → 4 — 20 fewer logical reads, an 83%
reduction (~6×), for the identical 7-row result.**

## 7. Final Validation

Final index inventory on `dbo.OrderActivity`, read from `sys.indexes` /
`sys.index_columns` after the experiment — confirming Task 1's three
indexes are untouched and this task's index now carries the `INCLUDE`:

| IndexName | IndexType | KeyColumns | IncludedCols |
|---|---|---|---|
| `CIX_OrderActivity_OrderDate` | CLUSTERED | `OrderDate` | *(none)* |
| `IX_OrderActivity_CustomerId` | NONCLUSTERED | `CustomerId` | `Amount` |
| `IX_OrderActivity_CustomerId_Status` | NONCLUSTERED | `CustomerId, Status` | `Amount, Notes, OrderDate` |
| `IX_OrderActivity_Status_OrderDate` | NONCLUSTERED | `Status, OrderDate` | `Amount` |

| Check | Result |
|---|---|
| Azure SQL execution | PASS |
| Table/data verified | PASS (104,000 rows, unchanged from Task 1) |
| Baseline query executed | PASS |
| Baseline plan shows Key Lookup | PASS (`IndexScan Lookup="1"` on `CIX_OrderActivity_OrderDate`) |
| Baseline logical reads captured | PASS (24, via `SET STATISTICS IO ON`) |
| Non-covering index definition shown | PASS (§1, read from `sys.indexes`/`sys.index_columns`) |
| Covering index created | PASS (`INCLUDE (OrderDate, Amount, Notes)`, confirmed in catalog) |
| After query executed (same query) | PASS |
| After plan inspected | PASS |
| Key Lookup confirmed gone | PASS (`CIX_OrderActivity_OrderDate` absent from the plan entirely) |
| After logical reads captured | PASS (4, via `SET STATISTICS IO ON`) |
| Before/after logical reads compared | PASS (24 → 4, §6 above) |
| Task 1's indexes left unmodified | PASS (confirmed in final inventory above) |

Day 8 Task 2 is complete; no remaining implementation work. This script
was executed twice end-to-end against the live database while writing
this section — both runs reproduced the same 24 → 4 logical-reads
result and the same Key-Lookup-present → Key-Lookup-absent plan shift.
