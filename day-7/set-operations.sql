--------------------------------------------------------------------------
-- Day 7 — Set operations from a spec
--------------------------------------------------------------------------
-- Dialect: SQL Server / Azure SQL Database (T-SQL).
--
-- Reuses the same Authors/Quotes schema and seed data created for the
-- Day 7 joins-and-CTEs exercise (see
-- ../day-1/QuotesApi/day-7/day7-joins-and-ctes.sql) — no new Authors or
-- Quotes rows, no in-memory or fabricated database. This script assumes
-- that schema already exists in the target database.
--
-- The three business questions below need two things the original
-- Authors/Quotes schema does not have: a way to tell a "classic" quote
-- from a "modern" one, and a tag vocabulary. So this script adds the
-- smallest additional schema needed for the set-operations exercise —
-- a nullable Quotes.Category column ('Classic' / 'Modern'), a Tags
-- table (each tag also belongs to a Category), and a QuoteTags junction
-- table for the many-to-many Quote <-> Tag relationship — on top of the
-- existing Authors/Quotes tables. It does not touch the application's
-- EF Core model, migrations, or the SQLite database the API actually
-- runs on. All statements are idempotent (guarded with IF checks) so the
-- script can be re-run safely.
--
-- Executed against: Azure SQL Database `thinkschool-day7` on logical
-- server `thinkschool-day7-sql-0c0dda.database.windows.net`
-- (resource group `thinkschool-rg`, region `centralindia`), via an Azure
-- AD access token for the signed-in `az login` identity — no SQL
-- login/password used or stored. Every result captured in README.md is
-- genuine output from that database, not invented.
--------------------------------------------------------------------------


--------------------------------------------------------------------------
-- 1. Schema additions (idempotent)
--------------------------------------------------------------------------

IF NOT EXISTS
(
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Quotes')
      AND name = N'Category'
)
BEGIN
    ALTER TABLE dbo.Quotes ADD Category NVARCHAR(20) NULL;
END
GO

IF OBJECT_ID(N'dbo.QuoteTags', N'U') IS NOT NULL
    DROP TABLE dbo.QuoteTags;

IF OBJECT_ID(N'dbo.Tags', N'U') IS NOT NULL
    DROP TABLE dbo.Tags;

CREATE TABLE dbo.Tags
(
    TagId       INT             IDENTITY (1, 1) NOT NULL,
    TagName     NVARCHAR(50)    NOT NULL,
    Category    NVARCHAR(20)    NOT NULL,

    CONSTRAINT PK_Tags PRIMARY KEY (TagId)
);

CREATE TABLE dbo.QuoteTags
(
    QuoteId     INT NOT NULL,
    TagId       INT NOT NULL,

    CONSTRAINT PK_QuoteTags PRIMARY KEY (QuoteId, TagId),
    CONSTRAINT FK_QuoteTags_Quotes FOREIGN KEY (QuoteId)
        REFERENCES dbo.Quotes (QuoteId),
    CONSTRAINT FK_QuoteTags_Tags FOREIGN KEY (TagId)
        REFERENCES dbo.Tags (TagId)
);
GO


--------------------------------------------------------------------------
-- 2. Seed / test data
--------------------------------------------------------------------------
-- Quotes.Category assigns each of the 9 existing quotes to the 'Classic'
-- or 'Modern' set. Two authors (Ava Thornton, Derek Osei) get a mix of
-- both categories across their quotes, so Query 2's INTERSECT has a real,
-- non-empty answer instead of an artificially forced one.
--
-- Tags/QuoteTags tag only Ava Thornton's, Baxter Lin's, and Derek Osei's
-- quotes, deliberately leaving Cleo Marsh's and Farid Haidari's quotes
-- untagged, so Query 1's EXCEPT has a real, non-empty answer.
--
-- The tag 'discipline' is seeded once under 'Classic' and again under
-- 'Modern' (two different TagId rows, same TagName) — a realistic case
-- of the same word being used as a tag in both categories — so Query 3's
-- UNION has a real duplicate to collapse via DISTINCT, not a
-- coincidence-free list that would pass even with UNION ALL.
--------------------------------------------------------------------------

UPDATE dbo.Quotes SET Category = 'Classic' WHERE QuoteId IN (1, 3, 6, 7);
UPDATE dbo.Quotes SET Category = 'Modern'  WHERE QuoteId IN (2, 4, 5, 8, 9);
GO

INSERT INTO dbo.Tags (TagName, Category) VALUES
    (N'discipline', N'Classic'),
    (N'resilience', N'Classic'),
    (N'growth',     N'Modern'),
    (N'feedback',   N'Modern'),
    (N'discipline', N'Modern');
GO

INSERT INTO dbo.QuoteTags (QuoteId, TagId)
SELECT 1, TagId FROM dbo.Tags WHERE TagName = N'discipline' AND Category = N'Classic'
UNION ALL
SELECT 3, TagId FROM dbo.Tags WHERE TagName = N'resilience' AND Category = N'Classic'
UNION ALL
SELECT 2, TagId FROM dbo.Tags WHERE TagName = N'growth'     AND Category = N'Modern'
UNION ALL
SELECT 4, TagId FROM dbo.Tags WHERE TagName = N'feedback'   AND Category = N'Modern'
UNION ALL
SELECT 5, TagId FROM dbo.Tags WHERE TagName = N'discipline' AND Category = N'Modern'
UNION ALL
SELECT 7, TagId FROM dbo.Tags WHERE TagName = N'discipline' AND Category = N'Classic'
UNION ALL
SELECT 8, TagId FROM dbo.Tags WHERE TagName = N'growth'     AND Category = N'Modern';
-- Quote 6 (Cleo Marsh) and Quote 9 (Farid Haidari) are left untagged on
-- purpose — see the block comment above.
GO


--------------------------------------------------------------------------
-- 3. Query 1 — Authors with quotes but no tags (EXCEPT)
--------------------------------------------------------------------------
-- Business question: "Find authors who have quotes but have no
-- associated tags."
--
-- Why EXCEPT: this is literally set subtraction — start from the set of
-- authors who have at least one quote, then remove every author who has
-- at least one *tagged* quote. EXCEPT returns exactly the rows on the
-- left that have no match on the right (by full row, after implicit
-- DISTINCT), which is precisely "in A, not in B" — there's no join
-- condition that expresses "has no tagged quote" as cleanly as a
-- NOT EXISTS/NOT IN would, but EXCEPT states the intent as a set
-- difference in one readable statement instead of a negated join.
--------------------------------------------------------------------------

SELECT a.AuthorId, a.Name
FROM dbo.Authors AS a
INNER JOIN dbo.Quotes AS q
    ON q.AuthorId = a.AuthorId

EXCEPT

SELECT a.AuthorId, a.Name
FROM dbo.Authors AS a
INNER JOIN dbo.Quotes AS q
    ON q.AuthorId = a.AuthorId
INNER JOIN dbo.QuoteTags AS qt
    ON qt.QuoteId = q.QuoteId

ORDER BY Name;
GO


--------------------------------------------------------------------------
-- 4. Query 2 — Authors in both 'classic' and 'modern' (INTERSECT)
--------------------------------------------------------------------------
-- Business question: "Find authors who have quotes in BOTH the
-- 'classic' and 'modern' sets."
--
-- Why INTERSECT: "in both sets" is exactly set intersection — the set of
-- authors with a Classic-category quote, intersected with the set of
-- authors with a Modern-category quote. INTERSECT returns only rows
-- present in *both* input sets (by full row, after implicit DISTINCT),
-- which matches the "BOTH" in the business question directly; a join on
-- AuthorId between the two sub-selects would need an extra DISTINCT and
-- reads less like the requirement than INTERSECT does.
--------------------------------------------------------------------------

SELECT a.AuthorId, a.Name
FROM dbo.Authors AS a
INNER JOIN dbo.Quotes AS q
    ON q.AuthorId = a.AuthorId
WHERE q.Category = 'Classic'

INTERSECT

SELECT a.AuthorId, a.Name
FROM dbo.Authors AS a
INNER JOIN dbo.Quotes AS q
    ON q.AuthorId = a.AuthorId
WHERE q.Category = 'Modern'

ORDER BY Name;
GO


--------------------------------------------------------------------------
-- 5. Query 3 — Combined distinct tag list across two categories (UNION)
--------------------------------------------------------------------------
-- Business question: "Return the combined DISTINCT tag list across two
-- categories."
--
-- Why UNION (not UNION ALL): the requirement explicitly says DISTINCT.
-- UNION already de-duplicates by definition, so it maps onto "combined
-- distinct list" with no extra DISTINCT keyword needed; UNION ALL would
-- keep the 'discipline' tag twice (once seeded under Classic, once under
-- Modern), which is the wrong answer to a "distinct" question.
--------------------------------------------------------------------------

SELECT TagName
FROM dbo.Tags
WHERE Category = 'Classic'

UNION

SELECT TagName
FROM dbo.Tags
WHERE Category = 'Modern'

ORDER BY TagName;
GO
