# QuotesApi — Week 1

This README currently documents the Day 7 exercise only. Earlier Week 1
days are documented individually under [`docs/`](docs/) and in
[`WHY.md`](WHY.md), [`DDD-AGGREGATE-NOTES.md`](DDD-AGGREGATE-NOTES.md), and
[`next-steps.md`](next-steps.md).

## Day 7 — Joins and CTEs at depth

> Most slow reports are a join done wrong. Get fluent in inner/left/cross
> joins and recursive + non-recursive CTEs against a real schema using
> Microsoft Azure SQL.

Full script: [Day 7 SQL exercise](docs/day7-joins-and-ctes.sql).

### What this builds

SQL fundamentals.

### Azure SQL Environment

- **Subscription:** Azure for Students (same subscription used for the Day
  5 deployment; see [`docs/day5-azd-deployment.md`](docs/day5-azd-deployment.md)).
- **Resource group:** `thinkschool-rg` (region: `centralindia`).
- **SQL logical server:** `thinkschool-day7-sql-0c0dda.database.windows.net`.
- **Database:** `thinkschool-day7` (Basic tier), provisioned on that
  server ahead of this session.
- **Firewall:** a rule scoped to this session's client IP only
  (`AllowClientIP-day7-session`).
- **Auth:** the server is mixed-mode (`azureADOnlyAuthentication: false`),
  but no SQL login password for it is stored anywhere. The connecting
  identity is instead the signed-in `az login` account
  (`ayush.sinha7@s.amity.edu`), which is the server's Azure AD admin —
  connection used an Azure AD access token (`az account get-access-token
  --resource https://database.windows.net`), not a SQL login/password.
- No password, connection string, or secret is stored in this repository
  or this document.

**What actually ran the query in this session:** the full script below
(`docs/day7-joins-and-ctes.sql` — schema, seed data, every join, both
CTEs, and the required exercise query) was executed end to end against
the real `thinkschool-day7` database on `thinkschool-day7-sql-0c0dda`,
via a Node.js `mssql`/Tedious client authenticated with the Azure AD
access token above. Every result captured in this document below is
genuine query output from that run, not invented.

### Schema

The application's real Quotes DB stores `Quote.Author` as a plain string
(see `Models/Quote.cs`) — there's no relational `Authors` table to join
against, and no author hierarchy. So this exercise adds the smallest
additional schema needed, as a standalone script
(`docs/day7-joins-and-ctes.sql`); it does not touch the EF Core model, the
app's migrations, or the SQLite database the API actually runs on.

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

### INNER JOIN

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

### LEFT JOIN

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

### CROSS JOIN

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

### Non-Recursive CTE

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

### Result — Top 10 Rows

The seed data only has 6 authors, so all 6 rows are returned (fewer than
10 — nothing is truncated).

Executed for real against the `thinkschool-day7` Azure SQL Database on
`thinkschool-day7-sql-0c0dda` (see [Azure SQL
Environment](#azure-sql-environment) above for the connection method).
The rows below are the genuine, unmodified output of that run — not
invented, not backfilled from another engine:

| AuthorId | Author | QuoteCount | MostRecentQuote |
|---:|---|---:|---|
| 1 | Ava Thornton | 3 | A calm mind sees the board more clearly than an anxious one. |
| 2 | Baxter Lin | 2 | Ask for feedback before you need it, not after you fail. |
| 4 | Derek Osei | 2 | The fastest way to learn is to teach it badly, then fix it. |
| 3 | Cleo Marsh | 1 | Curiosity is the discipline of asking one more question. |
| 6 | Farid Haidari | 1 | Write it down before you decide whether it is a good idea. |
| 5 | Elena Vranas | 0 | *(NULL)* |

Re-running [`docs/day7-joins-and-ctes.sql`](docs/day7-joins-and-ctes.sql)
end to end against the same database is deterministic (the script drops
and recreates `Authors`/`Quotes` first) and reproduces these same 6 rows
verbatim.

### Why a CTE here over a correlated subquery?

The alternative — `SELECT ..., (SELECT TOP 1 QuoteText FROM Quotes WHERE
AuthorId = a.AuthorId ORDER BY CreatedAt DESC)` in the `SELECT` list — runs
that subquery once per author row and mixes ranking logic into the
projection, which gets harder to read and to extend (e.g. adding "2nd most
recent quote" means a second near-duplicate subquery). The CTE instead
ranks every quote once, up front, separately from aggregation; the outer
query then just joins to that ranking and uses a conditional aggregate
(`MAX(CASE WHEN QuoteRank = 1 ...)`) to pull out rank 1's text alongside a
plain `COUNT`. Ranking and aggregating are two distinct, independently
readable steps instead of one query doing both at once, and there's no
per-row correlated subquery for the optimizer to (potentially) execute
once per outer row.

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

### Result

Also executed for real against the same `thinkschool-day7` database, in
the same run as the exercise query above:

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

Full script: [`docs/day7-joins-and-ctes.sql`](docs/day7-joins-and-ctes.sql)

## What I learned

- **INNER JOIN** only keeps rows that match on both sides — an author with
  no quotes disappears entirely.
- **LEFT JOIN** keeps every row from the left table regardless of a match,
  filling the right side with `NULL`s — the only way to see the zero-quote
  author (`Elena Vranas`) show up at all.
- **CROSS JOIN** builds every combination of rows from two sets (a
  Cartesian product); it's easy to accidentally explode row counts, so
  it's worth joining against something small and controlled on purpose.
- A **non-recursive CTE** is just a named, reusable `SELECT` — useful here
  to compute a ranking once and reference it later without repeating the
  window-function logic.
- **`ROW_NUMBER()`** assigns a unique, gapless rank per row within a
  window; **`PARTITION BY`** restarts that numbering for each group
  (here, each `AuthorId`), and ordering by `CreatedAt DESC` makes rank 1
  the most recent row in that group — that's the whole trick behind
  "latest row per group" without a subquery per group.
- **Aggregating after a CTE** (`GROUP BY` + `COUNT` + `MAX(CASE WHEN
  QuoteRank = 1 ...)`) lets ranking and aggregation stay as two separate,
  readable steps instead of one query doing both.
- A **recursive CTE** needs an anchor member (the base case — here, roots
  with no mentor) and a recursive member that joins the table back to the
  CTE itself; each pass can carry along a running `Depth` and a
  human-readable `Path` string.
- **Correlated subqueries in `SELECT`** run once per outer row, which gets
  harder to reason about and to extend (e.g. "also show the 2nd most
  recent quote" means writing a near-duplicate subquery); ranking once in
  a CTE up front is more explicit about what's happening and only needs
  writing once.
