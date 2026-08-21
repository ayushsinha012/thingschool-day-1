-- Before state, carried over from Day-11/task-1/queries.sql (Task 1's
-- baseline measurement) -- reproduced here so Task 2's before/after
-- comparison is readable without cross-referencing another task's folder.
-- Original capture: EF Core command logging
-- (Microsoft.EntityFrameworkCore.Database.Command=Information) for one
-- request to GET /api/quotes/performance/author-quotes?authors=50, against
-- the same day-1/QuotesApi/quotes.db (9,000 rows / 300 authors) used for
-- the Task 2 measurement in this directory.

-- Distinct-author query: issued exactly once per request.
SELECT "q0"."Author"
FROM (
    SELECT DISTINCT "q"."Author"
    FROM "Quotes" AS "q"
    WHERE NOT ("q"."IsDeleted")
) AS "q0"
ORDER BY "q0"."Author"
LIMIT @p;

-- Per-author query: issued once per author returned by the query above
-- (50 times for the default authors=50) -- this is the N+1.
-- Example parameter captured: @author = 'Synthetic Author 0001'
SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text"
FROM "Quotes" AS "q"
WHERE NOT ("q"."IsDeleted") AND "q"."Author" = @author;

-- Total: 1 + 50 = 51 Executed DbCommand entries for one request.
