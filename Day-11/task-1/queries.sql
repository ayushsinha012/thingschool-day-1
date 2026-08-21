-- Actual SQL emitted by EF Core for one request to
-- GET /api/quotes/performance/author-quotes?authors=50,
-- captured via Serilog EF Core command logging
-- (Microsoft.EntityFrameworkCore.Database.Command=Information)
-- against day-1/QuotesApi/quotes.db. See result.md for the full log context.

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
