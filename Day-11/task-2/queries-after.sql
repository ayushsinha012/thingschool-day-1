-- Optimized endpoint: GET /api/quotes/performance/author-quotes?authors=50
-- Captured with EF Core command logging enabled (Development environment,
-- Microsoft.EntityFrameworkCore.Database.Command=Debug), same quotes.db
-- (9,000 rows / 300 authors) used for the Task 1 baseline and this
-- measurement. One request now produces exactly ONE "Executed DbCommand"
-- log entry (down from 51 in the Task 1 baseline: 1 distinct-author query
-- + 50 sequential per-author queries).

SELECT "q"."Author", "q"."Text"
FROM "Quotes" AS "q"
WHERE NOT ("q"."IsDeleted") AND "q"."Author" IN (
    SELECT "q0"."Author"
    FROM (
        SELECT DISTINCT "q1"."Author"
        FROM "Quotes" AS "q1"
        WHERE NOT ("q1"."IsDeleted")
        ORDER BY "q1"."Author"
        LIMIT @authorCount
    ) AS "q0"
)
ORDER BY "q"."Author";

-- Measured SQLite execution time for this single command (EF Core command
-- logging, single-request, unloaded server): 1-4ms, vs. 51 x (mostly full
-- table scan) round trips in the Task 1 baseline.
