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
