# Day 7 — Joins and CTEs at depth

> Most slow reports are a join done wrong. Get fluent in inner/left/cross
> joins and recursive + non-recursive CTEs against a real schema using
> Microsoft Azure SQL.

This document summarizes the Day 7 SQL exercise. The full, runnable
script lives at
[`day-1/QuotesApi/day-7/day7-joins-and-ctes.sql`](../day-1/QuotesApi/day-7/day7-joins-and-ctes.sql)
(schema, seed data, every query below, and inline comments); this README
is the write-up of what that script contains and the actual results of
running it. Broader Day 7 narrative also lives in
[`day-1/QuotesApi/day-7/README.md`](../day-1/QuotesApi/day-7/README.md),
which documents the same exercise from inside the QuotesApi project
folder.

> A second Day 7 topic, window functions, is documented further down this
> page, in its own [Day 7 — Window functions](#day-7--window-functions)
> section, with its script at [`window-functions.sql`](./window-functions.sql)
> in this same directory.

## Azure SQL Database

- **Resource group:** `thinkschool-rg`
- **SQL logical server:** `thinkschool-day7-sql-0c0dda`
  (`thinkschool-day7-sql-0c0dda.database.windows.net`)
- **Database:** `thinkschool-day7` (Basic tier)
- **Region:** Central India (`centralindia`)
- **Subscription:** Azure for Students

Connection used an Azure AD access token for the signed-in `az login`
identity (that identity is the server's Azure AD admin) — no SQL login
password, connection string, or other secret is stored in this repository
or this document. The server is mixed-mode auth
(`azureADOnlyAuthentication: false`), but the SQL login's password is not
known to this session and was never needed.

**Every result in this document is genuine output from actually running
the script above against `thinkschool-day7` on Azure SQL** — none of it
is invented, approximated, or backfilled from another engine.

## Schema

The application's real Quotes DB (`day-1/QuotesApi`) stores `Quote.Author`
as a plain string — there's no relational `Authors` table to join
against, and no author hierarchy. This exercise adds the smallest
additional schema needed for Day 7, as a standalone script; it does not
touch the app's EF Core model, migrations, or the SQLite database the API
actually runs on.

- **`Authors`** — `AuthorId` (PK), `Name`, `MentorAuthorId` (nullable,
  self-referencing FK to `Authors.AuthorId`) for the recursive-CTE mentor
  hierarchy.
- **`Quotes`** — `QuoteId` (PK), `AuthorId` (FK to `Authors.AuthorId`),
  `QuoteText`, `CreatedAt`.
- **Relationship:** one author has many quotes (`Quotes.AuthorId ->
  Authors.AuthorId`).

Seed data: 6 authors, a two-level mentor hierarchy rooted at two authors
with no mentor, one author (`Elena Vranas`) with zero quotes, and 9 quotes
spread across different `CreatedAt` values. All author names are
synthetic test data, not real people.

## INNER JOIN

Demonstrates that an `INNER JOIN` returns only authors that have at least
one matching quote row — an author with zero quotes is dropped from the
result entirely.

```sql
SELECT
    a.AuthorId,
    a.Name,
    q.QuoteId,
    q.QuoteText,
    q.CreatedAt
FROM dbo.Authors AS a
INNER JOIN dbo.Quotes AS q
    ON q.AuthorId = a.AuthorId
ORDER BY
    a.Name,
    q.CreatedAt;
```

### Result (actual)

| AuthorId | Name | QuoteId | QuoteText | CreatedAt |
|---:|---|---:|---|---|
| 1 | Ava Thornton | 1 | Discipline is choosing what you want most over what you want now. | 2026-06-01 09:00:00 |
| 1 | Ava Thornton | 2 | Small steps, repeated daily, outrun sudden bursts of effort. | 2026-07-10 14:30:00 |
| 1 | Ava Thornton | 3 | A calm mind sees the board more clearly than an anxious one. | 2026-08-05 08:15:00 |
| 2 | Baxter Lin | 4 | Every system is perfectly designed to get the results it gets. | 2026-05-20 11:00:00 |
| 2 | Baxter Lin | 5 | Ask for feedback before you need it, not after you fail. | 2026-08-01 16:45:00 |
| 3 | Cleo Marsh | 6 | Curiosity is the discipline of asking one more question. | 2026-07-22 10:00:00 |
| 4 | Derek Osei | 7 | Momentum is a lagging indicator of consistency. | 2026-06-15 13:20:00 |
| 4 | Derek Osei | 8 | The fastest way to learn is to teach it badly, then fix it. | 2026-08-12 09:40:00 |
| 6 | Farid Haidari | 9 | Write it down before you decide whether it is a good idea. | 2026-07-30 17:10:00 |

`Elena Vranas` (zero quotes) does not appear — 9 rows total, one per
quote, no author row without a match.

## LEFT JOIN

Demonstrates that a `LEFT JOIN` keeps every row from the left table
(`Authors`) even when there is no matching quote — the author with zero
quotes remains visible, with `NULL` in every `Quotes` column.

```sql
SELECT
    a.AuthorId,
    a.Name,
    q.QuoteId,
    q.QuoteText,
    q.CreatedAt
FROM dbo.Authors AS a
LEFT JOIN dbo.Quotes AS q
    ON q.AuthorId = a.AuthorId
ORDER BY
    a.Name,
    q.CreatedAt;
```

### Result (actual)

| AuthorId | Name | QuoteId | QuoteText | CreatedAt |
|---:|---|---:|---|---|
| 1 | Ava Thornton | 1 | Discipline is choosing what you want most over what you want now. | 2026-06-01 09:00:00 |
| 1 | Ava Thornton | 2 | Small steps, repeated daily, outrun sudden bursts of effort. | 2026-07-10 14:30:00 |
| 1 | Ava Thornton | 3 | A calm mind sees the board more clearly than an anxious one. | 2026-08-05 08:15:00 |
| 2 | Baxter Lin | 4 | Every system is perfectly designed to get the results it gets. | 2026-05-20 11:00:00 |
| 2 | Baxter Lin | 5 | Ask for feedback before you need it, not after you fail. | 2026-08-01 16:45:00 |
| 3 | Cleo Marsh | 6 | Curiosity is the discipline of asking one more question. | 2026-07-22 10:00:00 |
| 4 | Derek Osei | 7 | Momentum is a lagging indicator of consistency. | 2026-06-15 13:20:00 |
| 4 | Derek Osei | 8 | The fastest way to learn is to teach it badly, then fix it. | 2026-08-12 09:40:00 |
| **5** | **Elena Vranas** | **NULL** | **NULL** | **NULL** |
| 6 | Farid Haidari | 9 | Write it down before you decide whether it is a good idea. | 2026-07-30 17:10:00 |

`Elena Vranas` (the zero-quote author) still shows up here, at rank
`AuthorId = 5`, with `NULL` `QuoteId`/`QuoteText`/`CreatedAt` — that row is
exactly the LEFT JOIN behavior this example is meant to demonstrate.

## CROSS JOIN

Demonstrates the Cartesian-product concept: every row on the left is
paired with every row on the right. Kept deliberately small — joined
against a tiny inline 2-row CTE rather than `Quotes` — so it stays
controlled instead of exploding into a huge result set.

```sql
WITH QuoteCategories AS
(
    SELECT 'Recent' AS Category
    UNION ALL
    SELECT 'Historical'
)
SELECT
    a.Name,
    c.Category
FROM dbo.Authors AS a
CROSS JOIN QuoteCategories AS c
ORDER BY
    a.Name,
    c.Category;
```

### Result (actual — 6 authors × 2 categories = 12 rows)

| Name | Category |
|---|---|
| Ava Thornton | Historical |
| Ava Thornton | Recent |
| Baxter Lin | Historical |
| Baxter Lin | Recent |
| Cleo Marsh | Historical |
| Cleo Marsh | Recent |
| Derek Osei | Historical |
| Derek Osei | Recent |
| Elena Vranas | Historical |
| Elena Vranas | Recent |
| Farid Haidari | Historical |
| Farid Haidari | Recent |

## Non-Recursive CTE

A CTE (`WITH ... AS (...)`) is just a named, reusable `SELECT`. Here it
computes a per-author ranking once, so it can be referenced later without
repeating the window-function logic:

- **`ROW_NUMBER()`** assigns a strictly increasing, gapless rank within
  each group — no ties, unlike `RANK()`/`DENSE_RANK()`.
- **`PARTITION BY q.AuthorId`** restarts that numbering at 1 for every
  author, so ranks are scoped per-author, not global across the whole
  table.
- **`ORDER BY q.CreatedAt DESC, q.QuoteId DESC`** — sorting by `CreatedAt`
  descending means the most recent row in each author's partition gets
  rank 1; `QuoteId DESC` is a deterministic tie-breaker for two quotes
  that happen to share an identical `CreatedAt`.
- Together, this is the whole trick behind "the latest row per group"
  without a per-group subquery: rank everything once, then filter/join on
  `QuoteRank = 1` downstream.

```sql
WITH RankedQuotes AS
(
    SELECT
        q.QuoteId,
        q.AuthorId,
        q.QuoteText,
        q.CreatedAt,
        ROW_NUMBER() OVER
        (
            PARTITION BY q.AuthorId
            ORDER BY q.CreatedAt DESC, q.QuoteId DESC
        ) AS QuoteRank
    FROM dbo.Quotes AS q
)
SELECT * FROM RankedQuotes ORDER BY AuthorId, QuoteRank;
```

### Result (actual)

| QuoteId | AuthorId | QuoteText | CreatedAt | QuoteRank |
|---:|---:|---|---|---:|
| 3 | 1 | A calm mind sees the board more clearly than an anxious one. | 2026-08-05 08:15:00 | 1 |
| 2 | 1 | Small steps, repeated daily, outrun sudden bursts of effort. | 2026-07-10 14:30:00 | 2 |
| 1 | 1 | Discipline is choosing what you want most over what you want now. | 2026-06-01 09:00:00 | 3 |
| 5 | 2 | Ask for feedback before you need it, not after you fail. | 2026-08-01 16:45:00 | 1 |
| 4 | 2 | Every system is perfectly designed to get the results it gets. | 2026-05-20 11:00:00 | 2 |
| 6 | 3 | Curiosity is the discipline of asking one more question. | 2026-07-22 10:00:00 | 1 |
| 8 | 4 | The fastest way to learn is to teach it badly, then fix it. | 2026-08-12 09:40:00 | 1 |
| 7 | 4 | Momentum is a lagging indicator of consistency. | 2026-06-15 13:20:00 | 2 |
| 9 | 6 | Write it down before you decide whether it is a good idea. | 2026-07-30 17:10:00 | 1 |

`Elena Vranas` (`AuthorId = 5`) has no rows here at all — she has zero
quotes to rank, which is expected and consistent with the LEFT JOIN
result above.

## Recursive CTE

A recursive CTE needs two parts:

- **Anchor member** — the base case. Here, the first `SELECT`, matching
  authors with `MentorAuthorId IS NULL` — the roots of the hierarchy, at
  `Depth = 0`.
- **Recursive member** — the second `SELECT`, which joins `Authors` back
  to the CTE itself (`AuthorHierarchy`) on `child.MentorAuthorId =
  parent.AuthorId`, so each pass only pulls in authors whose mentor was
  already found in the previous pass.
- **Hierarchy depth** — `parent.Depth + 1` increments by one on every
  recursive pass, so `Depth` is how many mentor hops separate an author
  from a root.
- **Hierarchy path** — `parent.Path + ' -> ' + child.Name` builds up a
  human-readable trail from root to that author as recursion proceeds.

```sql
WITH AuthorHierarchy AS
(
    SELECT
        AuthorId,
        Name,
        MentorAuthorId,
        0 AS Depth,
        CAST(Name AS NVARCHAR(MAX)) AS Path
    FROM dbo.Authors
    WHERE MentorAuthorId IS NULL

    UNION ALL

    SELECT
        child.AuthorId,
        child.Name,
        child.MentorAuthorId,
        parent.Depth + 1,
        CAST(parent.Path + N' -> ' + child.Name AS NVARCHAR(MAX))
    FROM dbo.Authors AS child
    INNER JOIN AuthorHierarchy AS parent
        ON child.MentorAuthorId = parent.AuthorId
)
SELECT AuthorId, Name, Depth, Path
FROM AuthorHierarchy
ORDER BY Depth, Name
OPTION (MAXRECURSION 100);
```

### Result (actual)

| AuthorId | Name | Depth | Path |
|---:|---|---:|---|
| 1 | Ava Thornton | 0 | Ava Thornton |
| 5 | Elena Vranas | 0 | Elena Vranas |
| 2 | Baxter Lin | 1 | Ava Thornton -> Baxter Lin |
| 3 | Cleo Marsh | 1 | Ava Thornton -> Cleo Marsh |
| 4 | Derek Osei | 2 | Ava Thornton -> Baxter Lin -> Derek Osei |
| 6 | Farid Haidari | 2 | Ava Thornton -> Cleo Marsh -> Farid Haidari |

Three depth levels (0, 1, 2) across two disjoint mentor trees rooted at
`Ava Thornton` and `Elena Vranas`, with no cycles — `Elena Vranas` is a
root with zero mentees, so she appears at `Depth = 0` with no children.

## Exercise

Build a query that, in one statement, returns each author with their
quote count and their most-recent quote — using a CTE, not a correlated
subquery in the SELECT.

### SQL

```sql
WITH RankedQuotes AS
(
    SELECT
        q.QuoteId,
        q.AuthorId,
        q.QuoteText,
        q.CreatedAt,
        ROW_NUMBER() OVER
        (
            PARTITION BY q.AuthorId
            ORDER BY q.CreatedAt DESC, q.QuoteId DESC
        ) AS QuoteRank
    FROM dbo.Quotes AS q
)
SELECT TOP (10)
    a.AuthorId,
    a.Name AS Author,
    COUNT(rq.QuoteId) AS QuoteCount,
    MAX
    (
        CASE
            WHEN rq.QuoteRank = 1
            THEN rq.QuoteText
        END
    ) AS MostRecentQuote
FROM dbo.Authors AS a
LEFT JOIN RankedQuotes AS rq
    ON rq.AuthorId = a.AuthorId
GROUP BY
    a.AuthorId,
    a.Name
ORDER BY
    QuoteCount DESC,
    a.Name;
```

### Result Set — Top 10 Rows

Executed for real against the `thinkschool-day7` Azure SQL Database on
`thinkschool-day7-sql-0c0dda`. The seed data only has 6 authors, so all 6
rows are returned (fewer than `TOP (10)` — nothing is truncated). These
rows are the genuine, unmodified output of that run:

| AuthorId | Author | QuoteCount | MostRecentQuote |
|---:|---|---:|---|
| 1 | Ava Thornton | 3 | A calm mind sees the board more clearly than an anxious one. |
| 2 | Baxter Lin | 2 | Ask for feedback before you need it, not after you fail. |
| 4 | Derek Osei | 2 | The fastest way to learn is to teach it badly, then fix it. |
| 3 | Cleo Marsh | 1 | Curiosity is the discipline of asking one more question. |
| 6 | Farid Haidari | 1 | Write it down before you decide whether it is a good idea. |
| 5 | Elena Vranas | 0 | *(NULL)* |

### Why a CTE here over a correlated subquery?

The CTE ranks every author's quotes once with `ROW_NUMBER()`, up front and
separately from aggregation, so the outer query just `GROUP BY`s and
aggregates against that ranking (`COUNT` + `MAX(CASE WHEN QuoteRank = 1
...)`) instead of running a `(SELECT TOP 1 ... WHERE AuthorId = a.AuthorId
...)` subquery once per author row in the `SELECT` list — one readable
pass instead of a per-row correlated lookup that gets harder to extend.

## Actual Execution / Validation Results

Every query on this page was executed in one script run
(`day7-joins-and-ctes.sql`, batch-separated by `GO`) against the live
`thinkschool-day7` Azure SQL Database, via a Node.js `mssql`/Tedious
client authenticated with an Azure AD access token
(`az account get-access-token --resource https://database.windows.net`).
The run completed with exit code 0 and zero failed batches:

| Step | Status |
|---|---|
| Schema creation (`Authors`, `Quotes`, FKs, index) | PASS |
| Seed data (6 authors, 9 quotes) | PASS |
| INNER JOIN | PASS |
| LEFT JOIN | PASS |
| CROSS JOIN | PASS |
| Non-recursive CTE (`ROW_NUMBER()` ranking) | PASS |
| Required author/quote-count/latest-quote query | PASS |
| Recursive CTE (mentor hierarchy) | PASS |

## Remaining Incomplete Items

None. All required Day 7 exercises — INNER JOIN, LEFT JOIN, CROSS JOIN,
the non-recursive CTE, the required author/quote-count/latest-quote
query, and the recursive CTE — have been executed successfully against
the real Azure SQL database above, and every result shown in this
document is the actual output of that run.

---

# Day 7 — Window functions

> Window functions are the difference between a junior and a senior SQL
> author.

This section covers the second Day 7 SQL topic: `ROW_NUMBER()`, `RANK()`,
`LAG()`, `LEAD()`, and a running total with `SUM() OVER (ORDER BY ...)`.
The full, runnable script lives at
[`window-functions.sql`](./window-functions.sql) in this same directory
(every query below, in one file, with inline comments).

This section builds on the exact same `Authors`/`Quotes` schema and seed
data from the [joins-and-CTEs exercise](#day-7--joins-and-ctes-at-depth)
above — no new tables, no new seed data, no in-memory or fabricated
database. `window-functions.sql` only adds `SELECT` queries against the
schema that already existed in Azure SQL from that earlier exercise.

## Azure SQL Database

- **Resource group:** `thinkschool-rg`
- **SQL logical server:** `thinkschool-day7-sql-0c0dda`
  (`thinkschool-day7-sql-0c0dda.database.windows.net`)
- **Database:** `thinkschool-day7` (Basic tier)
- **Region:** Central India (`centralindia`)
- **Subscription:** Azure for Students

Same database as the joins-and-CTEs exercise — connected with an Azure AD
access token for the signed-in `az login` identity (`az account
get-access-token --resource https://database.windows.net`), piped
directly into a short-lived Node.js `mssql` client session and never
written to disk. No SQL login, password, or connection string with
credentials is stored in this repository or this document.

**Every result in this section is genuine output from actually running
`window-functions.sql` against `thinkschool-day7` on Azure SQL** — none
of it is invented, approximated, or backfilled from another engine.

## Schema (reused)

- **`Authors`** — `AuthorId` (PK), `Name`, `MentorAuthorId` (nullable,
  self-referencing FK).
- **`Quotes`** — `QuoteId` (PK), `AuthorId` (FK to `Authors.AuthorId`),
  `QuoteText`, `CreatedAt`.

Same 6 authors / 9 quotes seed data as above (one author, `Elena Vranas`,
has zero quotes and so does not appear in any window-function result
below, since every query here joins from `Quotes`).

## ROW_NUMBER()

Numbers each author's quotes in creation order, restarting at 1 for every
author (`PARTITION BY AuthorId`, `ORDER BY CreatedAt`).

```sql
SELECT
    a.Name AS Author,
    q.QuoteId,
    q.QuoteText AS Quote,
    q.CreatedAt,
    ROW_NUMBER() OVER
    (
        PARTITION BY q.AuthorId
        ORDER BY q.CreatedAt ASC, q.QuoteId ASC
    ) AS RowNum
FROM dbo.Quotes AS q
INNER JOIN dbo.Authors AS a
    ON a.AuthorId = q.AuthorId
ORDER BY
    a.Name,
    RowNum;
```

### Result (actual)

| Author | QuoteId | Quote | CreatedAt | RowNum |
|---|---:|---|---|---:|
| Ava Thornton | 1 | Discipline is choosing what you want most over what you want now. | 2026-06-01 09:00:00 | 1 |
| Ava Thornton | 2 | Small steps, repeated daily, outrun sudden bursts of effort. | 2026-07-10 14:30:00 | 2 |
| Ava Thornton | 3 | A calm mind sees the board more clearly than an anxious one. | 2026-08-05 08:15:00 | 3 |
| Baxter Lin | 4 | Every system is perfectly designed to get the results it gets. | 2026-05-20 11:00:00 | 1 |
| Baxter Lin | 5 | Ask for feedback before you need it, not after you fail. | 2026-08-01 16:45:00 | 2 |
| Cleo Marsh | 6 | Curiosity is the discipline of asking one more question. | 2026-07-22 10:00:00 | 1 |
| Derek Osei | 7 | Momentum is a lagging indicator of consistency. | 2026-06-15 13:20:00 | 1 |
| Derek Osei | 8 | The fastest way to learn is to teach it badly, then fix it. | 2026-08-12 09:40:00 | 2 |
| Farid Haidari | 9 | Write it down before you decide whether it is a good idea. | 2026-07-30 17:10:00 | 1 |

Numbering restarts at 1 for each author, exactly as expected from
`PARTITION BY AuthorId`.

## RANK()

Ranks each author's quotes by quote length (`LEN(QuoteText)`), longest
first. Quote length is not a synthetic tie-breaker chosen to force a
result — it's the real data: `Ava Thornton`'s two shorter quotes (QuoteId
2 and 3) are both exactly 60 characters, a genuine tie in the existing
seed data.

```sql
SELECT
    a.Name AS Author,
    q.QuoteId,
    q.QuoteText AS Quote,
    LEN(q.QuoteText) AS QuoteLength,
    RANK() OVER
    (
        PARTITION BY q.AuthorId
        ORDER BY LEN(q.QuoteText) DESC
    ) AS LengthRank
FROM dbo.Quotes AS q
INNER JOIN dbo.Authors AS a
    ON a.AuthorId = q.AuthorId
ORDER BY
    a.Name,
    LengthRank,
    q.QuoteId;
```

### Result (actual)

| Author | QuoteId | Quote | QuoteLength | LengthRank |
|---|---:|---|---:|---:|
| Ava Thornton | 1 | Discipline is choosing what you want most over what you want now. | 65 | 1 |
| Ava Thornton | 2 | Small steps, repeated daily, outrun sudden bursts of effort. | 60 | 2 |
| Ava Thornton | 3 | A calm mind sees the board more clearly than an anxious one. | 60 | 2 |
| Baxter Lin | 4 | Every system is perfectly designed to get the results it gets. | 62 | 1 |
| Baxter Lin | 5 | Ask for feedback before you need it, not after you fail. | 56 | 2 |
| Cleo Marsh | 6 | Curiosity is the discipline of asking one more question. | 56 | 1 |
| Derek Osei | 8 | The fastest way to learn is to teach it badly, then fix it. | 59 | 1 |
| Derek Osei | 7 | Momentum is a lagging indicator of consistency. | 47 | 2 |
| Farid Haidari | 9 | Write it down before you decide whether it is a good idea. | 58 | 1 |

`Ava Thornton`'s two 60-character quotes (QuoteId 2 and 3) both land at
`LengthRank = 2` — the real tie in the data — and there is no
`LengthRank = 3` for her at all, since `RANK()` skips ranks after a tie.
That gap (1, 2, 2, — no 3) is exactly the behavior that distinguishes
`RANK()` from `ROW_NUMBER()`.

## LAG()

For each quote, looks back one row within the same author's partition
(ordered by `CreatedAt`) to pull the previous quote's date and text.

```sql
SELECT
    a.Name AS Author,
    q.QuoteId,
    q.QuoteText AS Quote,
    q.CreatedAt,
    LAG(q.CreatedAt) OVER
    (
        PARTITION BY q.AuthorId
        ORDER BY q.CreatedAt ASC, q.QuoteId ASC
    ) AS PreviousQuoteDate,
    LAG(q.QuoteText) OVER
    (
        PARTITION BY q.AuthorId
        ORDER BY q.CreatedAt ASC, q.QuoteId ASC
    ) AS PreviousQuoteText
FROM dbo.Quotes AS q
INNER JOIN dbo.Authors AS a
    ON a.AuthorId = q.AuthorId
ORDER BY
    a.Name,
    q.CreatedAt;
```

### Result (actual)

| Author | QuoteId | Quote | CreatedAt | PreviousQuoteDate | PreviousQuoteText |
|---|---:|---|---|---|---|
| Ava Thornton | 1 | Discipline is choosing what you want most over what you want now. | 2026-06-01 09:00:00 | NULL | NULL |
| Ava Thornton | 2 | Small steps, repeated daily, outrun sudden bursts of effort. | 2026-07-10 14:30:00 | 2026-06-01 09:00:00 | Discipline is choosing what you want most over what you want now. |
| Ava Thornton | 3 | A calm mind sees the board more clearly than an anxious one. | 2026-08-05 08:15:00 | 2026-07-10 14:30:00 | Small steps, repeated daily, outrun sudden bursts of effort. |
| Baxter Lin | 4 | Every system is perfectly designed to get the results it gets. | 2026-05-20 11:00:00 | NULL | NULL |
| Baxter Lin | 5 | Ask for feedback before you need it, not after you fail. | 2026-08-01 16:45:00 | 2026-05-20 11:00:00 | Every system is perfectly designed to get the results it gets. |
| Cleo Marsh | 6 | Curiosity is the discipline of asking one more question. | 2026-07-22 10:00:00 | NULL | NULL |
| Derek Osei | 7 | Momentum is a lagging indicator of consistency. | 2026-06-15 13:20:00 | NULL | NULL |
| Derek Osei | 8 | The fastest way to learn is to teach it badly, then fix it. | 2026-08-12 09:40:00 | 2026-06-15 13:20:00 | Momentum is a lagging indicator of consistency. |
| Farid Haidari | 9 | Write it down before you decide whether it is a good idea. | 2026-07-30 17:10:00 | NULL | NULL |

Every author's first quote (by `CreatedAt`) shows `NULL` for both
`PreviousQuoteDate` and `PreviousQuoteText` — there is no earlier row in
that partition, which is the correct `LAG()` behavior, not a bug.

## LEAD()

The mirror image of `LAG()`: looks forward one row within the same
author's partition to pull the next quote's date and text.

```sql
SELECT
    a.Name AS Author,
    q.QuoteId,
    q.QuoteText AS Quote,
    q.CreatedAt,
    LEAD(q.CreatedAt) OVER
    (
        PARTITION BY q.AuthorId
        ORDER BY q.CreatedAt ASC, q.QuoteId ASC
    ) AS NextQuoteDate,
    LEAD(q.QuoteText) OVER
    (
        PARTITION BY q.AuthorId
        ORDER BY q.CreatedAt ASC, q.QuoteId ASC
    ) AS NextQuoteText
FROM dbo.Quotes AS q
INNER JOIN dbo.Authors AS a
    ON a.AuthorId = q.AuthorId
ORDER BY
    a.Name,
    q.CreatedAt;
```

### Result (actual)

| Author | QuoteId | Quote | CreatedAt | NextQuoteDate | NextQuoteText |
|---|---:|---|---|---|---|
| Ava Thornton | 1 | Discipline is choosing what you want most over what you want now. | 2026-06-01 09:00:00 | 2026-07-10 14:30:00 | Small steps, repeated daily, outrun sudden bursts of effort. |
| Ava Thornton | 2 | Small steps, repeated daily, outrun sudden bursts of effort. | 2026-07-10 14:30:00 | 2026-08-05 08:15:00 | A calm mind sees the board more clearly than an anxious one. |
| Ava Thornton | 3 | A calm mind sees the board more clearly than an anxious one. | 2026-08-05 08:15:00 | NULL | NULL |
| Baxter Lin | 4 | Every system is perfectly designed to get the results it gets. | 2026-05-20 11:00:00 | 2026-08-01 16:45:00 | Ask for feedback before you need it, not after you fail. |
| Baxter Lin | 5 | Ask for feedback before you need it, not after you fail. | 2026-08-01 16:45:00 | NULL | NULL |
| Cleo Marsh | 6 | Curiosity is the discipline of asking one more question. | 2026-07-22 10:00:00 | NULL | NULL |
| Derek Osei | 7 | Momentum is a lagging indicator of consistency. | 2026-06-15 13:20:00 | 2026-08-12 09:40:00 | The fastest way to learn is to teach it badly, then fix it. |
| Derek Osei | 8 | The fastest way to learn is to teach it badly, then fix it. | 2026-08-12 09:40:00 | NULL | NULL |
| Farid Haidari | 9 | Write it down before you decide whether it is a good idea. | 2026-07-30 17:10:00 | NULL | NULL |

Every author's most recent quote shows `NULL` for both `NextQuoteDate`
and `NextQuoteText` — there is no later row in that partition.

## Running total

Cumulative character count of each author's quotes, in creation order,
using an explicit frame (`ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT
ROW`) — "every row from the start of this author's partition through the
current row."

```sql
SELECT
    a.Name AS Author,
    q.QuoteId,
    q.QuoteText AS Quote,
    q.CreatedAt,
    LEN(q.QuoteText) AS QuoteLength,
    SUM(LEN(q.QuoteText)) OVER
    (
        PARTITION BY q.AuthorId
        ORDER BY q.CreatedAt ASC, q.QuoteId ASC
        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
    ) AS RunningCharacterTotal
FROM dbo.Quotes AS q
INNER JOIN dbo.Authors AS a
    ON a.AuthorId = q.AuthorId
ORDER BY
    a.Name,
    q.CreatedAt;
```

### Result (actual)

| Author | QuoteId | CreatedAt | QuoteLength | RunningCharacterTotal |
|---|---:|---|---:|---:|
| Ava Thornton | 1 | 2026-06-01 09:00:00 | 65 | 65 |
| Ava Thornton | 2 | 2026-07-10 14:30:00 | 60 | 125 |
| Ava Thornton | 3 | 2026-08-05 08:15:00 | 60 | 185 |
| Baxter Lin | 4 | 2026-05-20 11:00:00 | 62 | 62 |
| Baxter Lin | 5 | 2026-08-01 16:45:00 | 56 | 118 |
| Cleo Marsh | 6 | 2026-07-22 10:00:00 | 56 | 56 |
| Derek Osei | 7 | 2026-06-15 13:20:00 | 47 | 47 |
| Derek Osei | 8 | 2026-08-12 09:40:00 | 59 | 106 |
| Farid Haidari | 9 | 2026-07-30 17:10:00 | 58 | 58 |

`RunningCharacterTotal` resets and accumulates independently per author
(e.g. Ava Thornton: 65 → 125 → 185; Baxter Lin: 62 → 118), confirming
`PARTITION BY AuthorId` scopes the running sum correctly.

## Exercise

Build a query that returns, per author, each quote with a running count
and the gap in days since their previous quote (`LAG()`).

### SQL

```sql
SELECT TOP (10)
    a.Name AS Author,
    q.QuoteText AS Quote,
    q.CreatedAt AS QuoteCreatedAt,
    COUNT(*) OVER
    (
        PARTITION BY q.AuthorId
        ORDER BY q.CreatedAt ASC, q.QuoteId ASC
        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
    ) AS RunningQuoteCount,
    LAG(q.CreatedAt) OVER
    (
        PARTITION BY q.AuthorId
        ORDER BY q.CreatedAt ASC, q.QuoteId ASC
    ) AS PreviousQuoteDate,
    DATEDIFF
    (
        DAY,
        LAG(q.CreatedAt) OVER
        (
            PARTITION BY q.AuthorId
            ORDER BY q.CreatedAt ASC, q.QuoteId ASC
        ),
        q.CreatedAt
    ) AS DaysSincePreviousQuote
FROM dbo.Quotes AS q
INNER JOIN dbo.Authors AS a
    ON a.AuthorId = q.AuthorId
ORDER BY
    a.Name,
    q.CreatedAt,
    q.QuoteId;
```

`RunningQuoteCount` is `COUNT(*)` over the same running-total frame as
the section above, just counting rows instead of summing a value.
`PreviousQuoteDate` comes straight from `LAG(q.CreatedAt)`, and
`DaysSincePreviousQuote` is `DATEDIFF(DAY, <that same LAG expression>,
q.CreatedAt)` — both reference the `LAG()` window function directly, with
no correlated subquery anywhere in the `SELECT` list.

### Result Set — Top 10 Rows (actual)

Executed for real against the `thinkschool-day7` Azure SQL Database on
`thinkschool-day7-sql-0c0dda`. The seed data has only 9 quotes, so `TOP
(10)` returns all 9 rows (fewer than 10 — nothing is truncated). These
rows are the genuine, unmodified output of that run:

| Author | Quote | QuoteCreatedAt | RunningQuoteCount | PreviousQuoteDate | DaysSincePreviousQuote |
|---|---|---|---:|---|---:|
| Ava Thornton | Discipline is choosing what you want most over what you want now. | 2026-06-01 09:00:00 | 1 | NULL | NULL |
| Ava Thornton | Small steps, repeated daily, outrun sudden bursts of effort. | 2026-07-10 14:30:00 | 2 | 2026-06-01 09:00:00 | 39 |
| Ava Thornton | A calm mind sees the board more clearly than an anxious one. | 2026-08-05 08:15:00 | 3 | 2026-07-10 14:30:00 | 26 |
| Baxter Lin | Every system is perfectly designed to get the results it gets. | 2026-05-20 11:00:00 | 1 | NULL | NULL |
| Baxter Lin | Ask for feedback before you need it, not after you fail. | 2026-08-01 16:45:00 | 2 | 2026-05-20 11:00:00 | 73 |
| Cleo Marsh | Curiosity is the discipline of asking one more question. | 2026-07-22 10:00:00 | 1 | NULL | NULL |
| Derek Osei | Momentum is a lagging indicator of consistency. | 2026-06-15 13:20:00 | 1 | NULL | NULL |
| Derek Osei | The fastest way to learn is to teach it badly, then fix it. | 2026-08-12 09:40:00 | 2 | 2026-06-15 13:20:00 | 58 |
| Farid Haidari | Write it down before you decide whether it is a good idea. | 2026-07-30 17:10:00 | 1 | NULL | NULL |

`RunningQuoteCount` climbs 1 → 2 → 3 within `Ava Thornton`'s rows and
independently resets to 1 at the start of every other author's rows.
Every author's first quote by `CreatedAt` has `PreviousQuoteDate = NULL`
and `DaysSincePreviousQuote = NULL` — expected, since `LAG()` has no
prior row to read for the first row of a partition. The day-gaps were
spot-checked by hand against the calendar, e.g. Ava Thornton's 2026-06-01
→ 2026-07-10 gap: 29 remaining days in June + 10 days into July = 39
days, matching `DaysSincePreviousQuote = 39`.

### Why use window functions here?

A correlated subquery version of this exercise would run, per row, a
`SELECT TOP 1 CreatedAt FROM Quotes WHERE AuthorId = q.AuthorId AND
CreatedAt < q.CreatedAt ORDER BY CreatedAt DESC` to find the previous
quote, plus a second correlated `COUNT(*) WHERE CreatedAt <=
q.CreatedAt` for the running count — two independent per-row lookups
against the same table, re-scanning and re-sorting each author's quotes
over and over as the outer query iterates. `LAG()` and `COUNT(*) OVER
(...)` compute both values in a single pass: SQL Server partitions and
sorts the rows into each author's window once, then walks that ordering
one time to produce every row's previous-value and running-count
results together. The SQL also reads as what it means — "the previous
row's date, in this ordering, within this author's group" — instead of
a nested query whose correlation predicate has to be reverse-engineered
to see that it's doing the same thing. That gap widens with data volume:
the correlated-subquery form's cost scales roughly quadratically with
quotes-per-author (a sub-select per row, each re-scanning that author's
rows), where the window-function form scales linearly (one sort, one
pass) — the same reason `RankedQuotes` in the joins/CTEs exercise above
replaced a per-author correlated lookup with a single `ROW_NUMBER()`
pass.

## Validation / Execution Status

Every query on this page (`window-functions.sql`, batch-separated by
`GO`) was executed in one script run against the live `thinkschool-day7`
Azure SQL Database, via a Node.js `mssql`/Tedious client authenticated
with an Azure AD access token (`az account get-access-token --resource
https://database.windows.net`), reusing the same `Authors`/`Quotes`
schema and data created by the joins-and-CTEs exercise above. The run
completed with exit code 0 and zero failed batches:

| Step | Status |
|---|---|
| `ROW_NUMBER()` per-author quote numbering | PASS |
| `RANK()` per-author quote-length ranking (with a real tie) | PASS |
| `LAG()` previous quote date/text | PASS |
| `LEAD()` next quote date/text | PASS |
| Running total (`SUM() OVER`, cumulative character count) | PASS |
| Required exercise (running count + `LAG()` day-gap) | PASS |

- `ROW_NUMBER()` numbering restarts at 1 per author and is gapless —
  confirmed above.
- `RANK()` shows a real tie (Ava Thornton, QuoteId 2 and 3, both
  `LengthRank = 2`) with the expected skipped rank afterward — confirmed
  above.
- `LAG()`/`LEAD()` values were cross-checked against the raw `Quotes`
  rows returned earlier in this repository's Day 7 work — each
  `PreviousQuoteDate`/`NextQuoteDate` matches the neighboring row's
  `CreatedAt` in that author's partition.
- The running `SUM()` accumulates correctly and independently per author
  — confirmed above.
- In the required exercise, `PreviousQuoteDate` is produced directly by
  `LAG(q.CreatedAt)` (visible in the SQL above), not a subquery, and
  `DaysSincePreviousQuote` was spot-checked by hand for correctness
  (see above).
- `NULL` appears for `PreviousQuoteDate`/`DaysSincePreviousQuote`
  exactly once per author — for that author's first quote by
  `CreatedAt` — which is the expected, correct result when no previous
  quote exists, not a defect.

## Remaining Day 7 Tasks

None. `ROW_NUMBER()`, `RANK()`, `LAG()`, `LEAD()`, the running total, and
the required per-author running-count/day-gap exercise have all been
implemented and executed successfully against the real Azure SQL
database above, with every result shown in this section being the
actual output of that run.
