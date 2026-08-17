--------------------------------------------------------------------------
-- Day 7 — Window functions
--------------------------------------------------------------------------
-- Dialect: SQL Server / Azure SQL Database (T-SQL).
--
-- Reuses the same Authors/Quotes schema and seed data created for the
-- Day 7 joins-and-CTEs exercise (see
-- ../day-1/QuotesApi/day-7/day7-joins-and-ctes.sql) — no new tables, no
-- new seed data. This script assumes that schema already exists in the
-- target database and only adds SELECT queries against it.
--
-- Executed against: Azure SQL Database `thinkschool-day7` on logical
-- server `thinkschool-day7-sql-0c0dda.database.windows.net`
-- (resource group `thinkschool-rg`, region `centralindia`), via an Azure
-- AD access token for the signed-in `az login` identity — no SQL
-- login/password used or stored. Every result captured in README.md is
-- genuine output from that database, not invented.
--------------------------------------------------------------------------


--------------------------------------------------------------------------
-- 1. ROW_NUMBER()
--------------------------------------------------------------------------
-- Numbers each author's quotes in creation order, restarting at 1 for
-- every author (PARTITION BY AuthorId). ROW_NUMBER() never ties — even
-- if two rows shared an ORDER BY key, they'd still get distinct,
-- sequential numbers, which is why QuoteId is added as a deterministic
-- tie-breaker.
--------------------------------------------------------------------------

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
GO


--------------------------------------------------------------------------
-- 2. RANK()
--------------------------------------------------------------------------
-- Ranks each author's quotes by quote length (LEN(QuoteText)), longest
-- first. Unlike the ordering columns used elsewhere in this script,
-- quote length is not unique per author in the real seed data — Ava
-- Thornton has two quotes that are both exactly 60 characters long — so
-- this metric actually exercises RANK()'s tie behavior: both 60-character
-- quotes receive the same rank, and the next distinct rank skips a
-- position (1, 2, 2, 4 — not 1, 2, 2, 3). That gap is what RANK() adds
-- over ROW_NUMBER().
--------------------------------------------------------------------------

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
GO


--------------------------------------------------------------------------
-- 3. LAG()
--------------------------------------------------------------------------
-- For each quote, looks back one row within the same author's partition
-- (ordered by CreatedAt) to pull the previous quote's date and text.
-- The first quote for each author has no predecessor, so LAG() returns
-- NULL for both columns on that row — expected, not a bug.
--------------------------------------------------------------------------

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
GO


--------------------------------------------------------------------------
-- 4. LEAD()
--------------------------------------------------------------------------
-- The mirror image of LAG(): looks forward one row within the same
-- author's partition to pull the next quote's date and text. The most
-- recent quote for each author has no successor, so LEAD() returns NULL
-- for both columns on that row.
--------------------------------------------------------------------------

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
GO


--------------------------------------------------------------------------
-- 5. Running total
--------------------------------------------------------------------------
-- Cumulative character count of each author's quotes, in creation order.
-- ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW makes the frame
-- explicit: "every row from the start of this author's partition up to
-- and including the current row" — the running-total shape.
--------------------------------------------------------------------------

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
GO


--------------------------------------------------------------------------
-- 6. Required exercise
--------------------------------------------------------------------------
-- Exercise: per author, each quote with a running count and the gap in
-- days since their previous quote (LAG) — no correlated subquery in the
-- SELECT list.
--
-- RunningQuoteCount uses COUNT(*) OVER (... ROWS BETWEEN UNBOUNDED
-- PRECEDING AND CURRENT ROW) — the running-total pattern from section 5,
-- counting rows instead of summing a value.
--
-- PreviousQuoteDate comes directly from LAG(q.CreatedAt) over the same
-- per-author, CreatedAt-ordered window. DaysSincePreviousQuote is
-- DATEDIFF(DAY, <that LAG value>, q.CreatedAt) — both reference the same
-- LAG() window function, not a subquery. For each author's first quote,
-- LAG() has no preceding row to look at, so PreviousQuoteDate and
-- DaysSincePreviousQuote are both NULL — that NULL is the expected,
-- correct output for "no previous quote exists" and is not an error.
--------------------------------------------------------------------------

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
GO
