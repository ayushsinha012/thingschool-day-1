--------------------------------------------------------------------------
-- Day 7 — Joins and CTEs at depth
--------------------------------------------------------------------------
-- Dialect: SQL Server / Azure SQL Database (T-SQL).
--
-- The Week 1 Quotes DB (day-1/QuotesApi) stores Author as a plain string
-- column on Quotes (see Models/Quote.cs) — there is no relational Authors
-- table to join against. That is the right shape for the app (quotes are
-- freeform, authors are not first-class aggregates), but it is not enough
-- to practice joins/CTEs across a real Author <-> Quote relationship, and
-- it has no author hierarchy at all for the recursive-CTE requirement.
--
-- So this script adds the smallest additional schema needed for Day 7 —
-- Authors and Quotes tables with a proper foreign key, plus a nullable
-- self-referencing MentorAuthorId for the recursive CTE — as a standalone
-- exercise schema. It does not touch the application's EF Core model,
-- migrations, or the SQLite database the API actually runs on.
--
-- Run this against a dedicated database (Azure SQL Database or any
-- SQL Server instance). No USE/CREATE DATABASE statement is included:
-- Azure SQL connections are already scoped to a single target database,
-- so switching databases is a connection-string concern, not a script
-- concern.
--
-- Executed against: Azure SQL Database `thinkschool-day7` on logical
-- server `thinkschool-day7-sql-0c0dda.database.windows.net`
-- (resource group `thinkschool-rg`, region `centralindia`), via an Azure
-- AD access token for the signed-in `az login` identity (that identity is
-- the server's Azure AD admin) — no SQL login/password used or stored.
-- Every result captured in README.md is genuine output from that
-- database, not invented.
--------------------------------------------------------------------------


--------------------------------------------------------------------------
-- 1. Schema / setup
--------------------------------------------------------------------------

IF OBJECT_ID(N'dbo.Quotes', N'U') IS NOT NULL
    DROP TABLE dbo.Quotes;

IF OBJECT_ID(N'dbo.Authors', N'U') IS NOT NULL
    DROP TABLE dbo.Authors;

CREATE TABLE dbo.Authors
(
    AuthorId        INT             IDENTITY (1, 1) NOT NULL,
    Name            NVARCHAR(200)   NOT NULL,
    MentorAuthorId  INT             NULL,

    CONSTRAINT PK_Authors PRIMARY KEY (AuthorId),
    CONSTRAINT FK_Authors_Mentor FOREIGN KEY (MentorAuthorId)
        REFERENCES dbo.Authors (AuthorId)
);

CREATE TABLE dbo.Quotes
(
    QuoteId     INT             IDENTITY (1, 1) NOT NULL,
    AuthorId    INT             NOT NULL,
    QuoteText   NVARCHAR(1000)  NOT NULL,
    CreatedAt   DATETIME2       NOT NULL,

    CONSTRAINT PK_Quotes PRIMARY KEY (QuoteId),
    CONSTRAINT FK_Quotes_Authors FOREIGN KEY (AuthorId)
        REFERENCES dbo.Authors (AuthorId)
);

CREATE INDEX IX_Quotes_AuthorId ON dbo.Quotes (AuthorId);
GO


--------------------------------------------------------------------------
-- 2. Seed / test data
--------------------------------------------------------------------------
-- 6 authors, a two-level mentor hierarchy rooted at two authors with no
-- mentor, one author (Elena Vranas) with zero quotes, and 9 quotes spread
-- across different CreatedAt values so ranking/ordering is meaningful.
-- Names are synthetic — not real people — chosen only to make the join
-- and hierarchy output readable.
--------------------------------------------------------------------------

SET IDENTITY_INSERT dbo.Authors ON;

INSERT INTO dbo.Authors (AuthorId, Name, MentorAuthorId) VALUES
    (1, N'Ava Thornton',   NULL),  -- root
    (2, N'Baxter Lin',     1),
    (3, N'Cleo Marsh',     1),
    (4, N'Derek Osei',     2),
    (5, N'Elena Vranas',   NULL),  -- root, zero quotes
    (6, N'Farid Haidari',  3);

SET IDENTITY_INSERT dbo.Authors OFF;
GO

SET IDENTITY_INSERT dbo.Quotes ON;

INSERT INTO dbo.Quotes (QuoteId, AuthorId, QuoteText, CreatedAt) VALUES
    (1, 1, N'Discipline is choosing what you want most over what you want now.', '2026-06-01T09:00:00'),
    (2, 1, N'Small steps, repeated daily, outrun sudden bursts of effort.',        '2026-07-10T14:30:00'),
    (3, 1, N'A calm mind sees the board more clearly than an anxious one.',       '2026-08-05T08:15:00'),
    (4, 2, N'Every system is perfectly designed to get the results it gets.',     '2026-05-20T11:00:00'),
    (5, 2, N'Ask for feedback before you need it, not after you fail.',           '2026-08-01T16:45:00'),
    (6, 3, N'Curiosity is the discipline of asking one more question.',          '2026-07-22T10:00:00'),
    (7, 4, N'Momentum is a lagging indicator of consistency.',                    '2026-06-15T13:20:00'),
    (8, 4, N'The fastest way to learn is to teach it badly, then fix it.',        '2026-08-12T09:40:00'),
    (9, 6, N'Write it down before you decide whether it is a good idea.',        '2026-07-30T17:10:00');

SET IDENTITY_INSERT dbo.Quotes OFF;
GO


--------------------------------------------------------------------------
-- 3. INNER JOIN
--------------------------------------------------------------------------
-- INNER JOIN returns only authors that have matching quote rows.
-- Elena Vranas (zero quotes) is dropped from the result entirely.
--------------------------------------------------------------------------

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
GO


--------------------------------------------------------------------------
-- 4. LEFT JOIN
--------------------------------------------------------------------------
-- LEFT JOIN keeps every author row even when there is no matching quote.
-- Elena Vranas still appears here, with NULL QuoteId/QuoteText/CreatedAt —
-- that NULL row is exactly what proves the LEFT JOIN semantics, and is
-- why the seed data deliberately includes a zero-quote author.
--------------------------------------------------------------------------

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
GO


--------------------------------------------------------------------------
-- 5. CROSS JOIN
--------------------------------------------------------------------------
-- CROSS JOIN produces every combination of rows from both sides (a
-- Cartesian product). Kept small on purpose: 6 authors x 2 categories =
-- 12 rows, not a join against Quotes, so it stays controlled.
--------------------------------------------------------------------------

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
GO


--------------------------------------------------------------------------
-- 6. Non-recursive CTE
--------------------------------------------------------------------------
-- Ranks every quote within its author's quotes, most recent first, using
-- ROW_NUMBER() OVER (PARTITION BY AuthorId ORDER BY CreatedAt DESC).
-- QuoteId DESC is a tie-breaker for quotes sharing an identical CreatedAt.
-- This CTE is the building block the required final query (section 7)
-- reuses to find "the most recent quote per author" without a correlated
-- subquery.
--------------------------------------------------------------------------

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
SELECT
    *
FROM RankedQuotes
ORDER BY
    AuthorId,
    QuoteRank;
GO


--------------------------------------------------------------------------
-- 7. Required exercise query
--------------------------------------------------------------------------
-- Exercise: return each author with their quote count and their
-- most-recent quote, in one statement, using a CTE — not a correlated
-- subquery in the SELECT list.
--
-- RankedQuotes ranks every author's quotes once (QuoteRank = 1 is the
-- most recent). The outer query LEFT JOINs Authors to that ranking,
-- COUNTs the matched QuoteIds per author for QuoteCount, and pulls the
-- QuoteRank = 1 row's text via MAX(CASE WHEN QuoteRank = 1 THEN ...) —
-- a conditional aggregate, not a per-row correlated subquery.
--------------------------------------------------------------------------

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
GO


--------------------------------------------------------------------------
-- 8. Recursive CTE
--------------------------------------------------------------------------
-- Walks the mentor hierarchy from the roots (MentorAuthorId IS NULL) down.
-- Anchor member: the root authors, Depth 0.
-- Recursive member: joins Authors back to the CTE on child.MentorAuthorId
-- = parent.AuthorId, incrementing Depth and extending Path each pass.
-- Seed data has two disjoint roots (Ava Thornton, Elena Vranas) and no
-- cycles, so recursion terminates naturally; MAXRECURSION is set anyway
-- as a defensive cap.
--------------------------------------------------------------------------

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
SELECT
    AuthorId,
    Name,
    Depth,
    Path
FROM AuthorHierarchy
ORDER BY
    Depth,
    Name
OPTION (MAXRECURSION 100);
GO


--------------------------------------------------------------------------
-- 9. Actual result notes
--------------------------------------------------------------------------
-- Seed data has exactly 6 authors, so the required query's TOP (10)
-- returns all 6 rows (fewer than 10 — nothing is truncated).
--
-- Actual result — genuinely executed against Azure SQL Database
-- `thinkschool-day7` on `thinkschool-day7-sql-0c0dda`, by AuthorId, Author,
-- QuoteCount, MostRecentQuote (ties broken alphabetically by Author):
--
--   1, Ava Thornton   -> 3 quotes, most recent = "A calm mind sees the board more clearly than an anxious one."
--   2, Baxter Lin     -> 2 quotes, most recent = "Ask for feedback before you need it, not after you fail."
--   4, Derek Osei     -> 2 quotes, most recent = "The fastest way to learn is to teach it badly, then fix it."
--   3, Cleo Marsh     -> 1 quote,  most recent = "Curiosity is the discipline of asking one more question."
--   6, Farid Haidari  -> 1 quote,  most recent = "Write it down before you decide whether it is a good idea."
--   5, Elena Vranas   -> 0 quotes, MostRecentQuote = NULL
--
-- Full run notes, connection method, and every captured result set are in
-- README.md under "Day 7" -> "Exercise" -> "Result".
--------------------------------------------------------------------------
