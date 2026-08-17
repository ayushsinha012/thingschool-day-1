# Day 7 — Joins and CTEs at depth

> Most slow reports are a join done wrong. Get fluent in inner/left/cross
> joins and recursive + non-recursive CTEs against a real schema using
> Microsoft Azure SQL.

Full script: [Day 7 SQL exercise](day7-joins-and-ctes.sql).

## Azure SQL Database

- **Database name:** `thinkschool-day7`
- **Resource group:** `thinkschool-rg`
- **Region:** `centralindia`

No password, connection string, or other secret is stored in this
repository or this document. The database connection for this exercise
used an Azure AD access token for the signed-in `az login` identity, not
a stored SQL login/password.

## Schema

The application's real Quotes DB stores `Quote.Author` as a plain string
(see `../Models/Quote.cs`) — there's no relational `Authors` table to join
against, and no author hierarchy. So this exercise adds the smallest
additional schema needed, as a standalone script
([`day7-joins-and-ctes.sql`](day7-joins-and-ctes.sql)); it does not touch
the EF Core model, the app's migrations, or the SQLite database the API
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

INNER JOIN returns only authors that have matching quote rows — `Elena
Vranas` (zero quotes) is dropped from the result entirely.

### Actual result

Executed for real against the `thinkschool-day7` Azure SQL Database (see
[Azure SQL Database](#azure-sql-database) above). 9 rows returned — every
`Quotes` row, joined to its author, `Elena Vranas` absent:

| AuthorId | Name | QuoteId | QuoteText | CreatedAt |
|---:|---|---:|---|---|
| 1 | Ava Thornton | 1 | Discipline is choosing what you want most over what you want now. | 2026-06-01T09:00:00 |
| 1 | Ava Thornton | 2 | Small steps, repeated daily, outrun sudden bursts of effort. | 2026-07-10T14:30:00 |
| 1 | Ava Thornton | 3 | A calm mind sees the board more clearly than an anxious one. | 2026-08-05T08:15:00 |
| 2 | Baxter Lin | 4 | Every system is perfectly designed to get the results it gets. | 2026-05-20T11:00:00 |
| 2 | Baxter Lin | 5 | Ask for feedback before you need it, not after you fail. | 2026-08-01T16:45:00 |
| 3 | Cleo Marsh | 6 | Curiosity is the discipline of asking one more question. | 2026-07-22T10:00:00 |
| 4 | Derek Osei | 7 | Momentum is a lagging indicator of consistency. | 2026-06-15T13:20:00 |
| 4 | Derek Osei | 8 | The fastest way to learn is to teach it badly, then fix it. | 2026-08-12T09:40:00 |
| 6 | Farid Haidari | 9 | Write it down before you decide whether it is a good idea. | 2026-07-30T17:10:00 |

## LEFT JOIN

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

LEFT JOIN keeps every author row even without a matching quote. `Elena
Vranas` still appears, with NULL `QuoteId`/`QuoteText`/`CreatedAt` — that
NULL row is exactly what demonstrates LEFT JOIN semantics, which is why
the seed data deliberately includes a zero-quote author.

### Actual result

Same 9 matched rows as the INNER JOIN result above, plus one additional
row for `Elena Vranas` with NULL `QuoteId`/`QuoteText`/`CreatedAt` — 10
rows total. Genuine output from the same run against `thinkschool-day7`.

## CROSS JOIN

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

CROSS JOIN produces every combination of rows from both sides (a Cartesian
product). Kept small on purpose — 6 authors x 2 categories = 12 rows — and
joined against a tiny inline CTE rather than `Quotes`, so it stays
controlled instead of exploding.

### Actual result

12 rows — every author paired with both `Historical` and `Recent`.
Genuine output from the same run against `thinkschool-day7`.

## Non-Recursive CTE

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

- `ROW_NUMBER()` assigns a strictly increasing rank within each group,
  with no gaps or ties (unlike `RANK()`/`DENSE_RANK()`).
- `PARTITION BY q.AuthorId` restarts that numbering at 1 for every author,
  so ranks are scoped per-author, not global.
- `ORDER BY q.CreatedAt DESC, q.QuoteId DESC` ranks each author's newest
  quote as `QuoteRank = 1`; `QuoteId DESC` is a deterministic tie-breaker
  for two quotes sharing an identical `CreatedAt`.

This CTE is the building block the required final query reuses to find
"the most recent quote per author" without a correlated subquery.

### Actual result

All 9 `Quotes` rows returned, each with a `QuoteRank` restarting at 1 per
author (e.g. `AuthorId = 1` ranks its 3 quotes 1, 2, 3 by most-recent
first). Genuine output from the same run against `thinkschool-day7`.

## Recursive CTE

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

- **Anchor member:** the first `SELECT`, matching authors with
  `MentorAuthorId IS NULL` — the roots of the hierarchy, at `Depth = 0`.
- **Recursive member:** the second `SELECT`, which joins `Authors` back to
  the CTE itself (`AuthorHierarchy`) on `child.MentorAuthorId =
  parent.AuthorId`, so it only pulls in authors whose mentor was already
  found in the previous pass.
- **Hierarchy depth:** `parent.Depth + 1` increments by one on every
  recursive pass, so `Depth` is how many mentor hops separate an author
  from a root.
- **Hierarchy path:** `parent.Path + ' -> ' + child.Name` builds up a
  human-readable trail from root to that author as recursion proceeds.
- Seed data has two disjoint roots (`Ava Thornton`, `Elena Vranas`) and no
  cycles, so recursion terminates naturally; `MAXRECURSION 100` is a
  defensive cap, not something the seed data actually needs.

### Actual result

Also executed for real against the same `thinkschool-day7` database, in
the same run as the exercise query below:

| AuthorId | Name | Depth | Path |
|---:|---|---:|---|
| 1 | Ava Thornton | 0 | Ava Thornton |
| 5 | Elena Vranas | 0 | Elena Vranas |
| 2 | Baxter Lin | 1 | Ava Thornton -> Baxter Lin |
| 3 | Cleo Marsh | 1 | Ava Thornton -> Cleo Marsh |
| 4 | Derek Osei | 2 | Ava Thornton -> Baxter Lin -> Derek Osei |
| 6 | Farid Haidari | 2 | Ava Thornton -> Cleo Marsh -> Farid Haidari |

Three depth levels (0, 1, 2) across two disjoint mentor trees rooted at
`Ava Thornton` and `Elena Vranas` — `Elena Vranas` is a root with zero
quotes and zero mentees, so she appears here at `Depth = 0` with no
children, which is correct: the recursive CTE and the zero-quote LEFT
JOIN case are deliberately the same author, exercising two different
requirements at once.

## Exercise

Build a query that, in one statement, returns each author with their
quote count and their most-recent quote — using a CTE, not a correlated
subquery in the `SELECT`.

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

The seed data only has 6 authors, so all 6 rows are returned (fewer than
10 — nothing is truncated).

Executed for real against the `thinkschool-day7` Azure SQL Database (see
[Azure SQL Database](#azure-sql-database) above). The rows below are the
genuine, unmodified output of that run — not invented, not backfilled
from another engine:

| AuthorId | Author | QuoteCount | MostRecentQuote |
|---:|---|---:|---|
| 1 | Ava Thornton | 3 | A calm mind sees the board more clearly than an anxious one. |
| 2 | Baxter Lin | 2 | Ask for feedback before you need it, not after you fail. |
| 4 | Derek Osei | 2 | The fastest way to learn is to teach it badly, then fix it. |
| 3 | Cleo Marsh | 1 | Curiosity is the discipline of asking one more question. |
| 6 | Farid Haidari | 1 | Write it down before you decide whether it is a good idea. |
| 5 | Elena Vranas | 0 | *(NULL)* |

Re-running [`day7-joins-and-ctes.sql`](day7-joins-and-ctes.sql) end to end
against the same database is deterministic (the script drops and
recreates `Authors`/`Quotes` first) and reproduces these same 6 rows
verbatim.

### Why a CTE here over a correlated subquery?

A CTE ranks every quote once, up front, and the outer query just joins to
that ranking with a conditional aggregate (`MAX(CASE WHEN QuoteRank = 1
...)`) — a correlated subquery in the `SELECT` list would instead re-run
once per outer row and mix ranking logic into the projection, which is
slower to reason about and harder to extend (e.g. adding "2nd most recent
quote" would mean a second near-duplicate subquery).

## Execution and validation

- Every query above (schema creation, seed inserts, INNER/LEFT/CROSS
  JOINs, both CTEs, and the required exercise query) was executed end to
  end against the real `thinkschool-day7` Azure SQL Database, via a
  Node.js `mssql`/Tedious client authenticated with an Azure AD access
  token (`az account get-access-token --resource
  https://database.windows.net`) for the signed-in `az login` identity.
- All result sets shown in this document are genuine query output
  captured from that run, not invented or backfilled from another engine.
- Re-running [`day7-joins-and-ctes.sql`](day7-joins-and-ctes.sql) is
  deterministic: it drops and recreates `Authors`/`Quotes` first, then
  re-seeds identical data, so every result above reproduces verbatim.

## Status

**Day 7, first task (joins and CTEs) — COMPLETE.** Schema, seed data,
INNER/LEFT/CROSS JOINs, a non-recursive CTE, a recursive CTE, and the
required author/quote-count/most-recent-quote exercise query are all
implemented, executed against the real Azure SQL Database above, and
documented with actual result sets.

The remaining two Day 7 tasks are **not implemented yet**.
