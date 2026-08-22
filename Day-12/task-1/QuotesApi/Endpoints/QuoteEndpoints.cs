using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using QuotesApi.Application.Quotes;
using QuotesApi.Authorization;
using QuotesApi.Data;
using QuotesApi.DTOs;
using QuotesApi.Repositories;
using QuotesApi.Validation;

namespace QuotesApi.Endpoints;

public sealed class QuoteEndpointsLogCategory;

public static class QuoteEndpoints
{
    public static void MapQuoteEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/quotes");

        group.MapGet(
            "/",
            async (
                int? page,
                int? size,
                IQuoteRepository repository,
                ILogger<QuoteEndpointsLogCategory> logger,
                CancellationToken cancellationToken) =>
            {
                var currentPage = page.GetValueOrDefault(1);
                var pageSize = size.GetValueOrDefault(10);

                if (currentPage < 1 ||
                    pageSize < 1 ||
                    pageSize > 100)
                {
                    logger.LogWarning(
                        "Rejected quote listing request with page {Page} size {Size}",
                        currentPage,
                        pageSize);

                    return Results.BadRequest(
                        new ProblemDetails
                        {
                            Title = "Invalid pagination",
                            Detail =
                                "Page must be at least 1 and size must be between 1 and 100."
                        });
                }

                var result = await repository.GetPagedAsync(
                    currentPage,
                    pageSize,
                    cancellationToken);

                logger.LogInformation(
                    "Listed quotes page {Page} size {Size}, returned {Count} of {Total}",
                    currentPage,
                    pageSize,
                    result.Items.Count(),
                    result.Total);

                return Results.Ok(
                    new
                    {
                        page = currentPage,
                        size = pageSize,
                        total = result.Total,
                        items = result.Items
                    });
            });

        group.MapGet(
            "/performance/author-quotes",
            async (
                int? authors,
                AppDbContext db,
                CancellationToken cancellationToken) =>
            {
                var authorCount = authors.GetValueOrDefault(50);

                if (authorCount < 1 || authorCount > 100)
                {
                    return Results.BadRequest(
                        new ProblemDetails
                        {
                            Title = "Invalid author count",
                            Detail = "Authors must be between 1 and 100."
                        });
                }

                // Selected authors as a subquery (not materialized client-side)
                // so the whole thing runs as ONE round trip: "WHERE Author IN
                // (SELECT DISTINCT Author ... ORDER BY Author LIMIT @authorCount)"
                // instead of the two round trips this endpoint used to make
                // (fetch author names, then fetch their quotes). Also projects
                // straight to (Author, Text) instead of full Quote entities so
                // the unused Id/IsDeleted columns aren't read, allocated, or
                // JSON-serialized for every one of the ~authorCount * 30 rows
                // returned.
                var selectedAuthors = db.Quotes
                    .AsNoTracking()
                    .Where(quote => !quote.IsDeleted)
                    .Select(quote => quote.Author)
                    .Distinct()
                    .OrderBy(author => author)
                    .Take(authorCount);

                var quotesForAuthors = await db.Quotes
                    .AsNoTracking()
                    .Where(quote => !quote.IsDeleted && selectedAuthors.Contains(quote.Author))
                    .OrderBy(quote => quote.Author)
                    .Select(quote => new { quote.Author, quote.Text })
                    .ToListAsync(cancellationToken);

                // Rows arrive pre-sorted by Author (from the ORDER BY above),
                // so a single linear pass groups them without building a
                // second hash lookup (ToLookup) over the whole result set.
                var result = new List<object>(authorCount);
                string? currentAuthor = null;
                List<string>? currentQuotes = null;

                foreach (var row in quotesForAuthors)
                {
                    if (currentAuthor != row.Author)
                    {
                        currentAuthor = row.Author;
                        currentQuotes = new List<string>();
                        result.Add(new { author = currentAuthor, quotes = currentQuotes });
                    }

                    currentQuotes!.Add(row.Text);
                }

                return Results.Ok(result);
            });

        group.MapPost(
            "/",
            async (
                CreateQuoteRequest request,
                IMediator mediator,
                ILogger<QuoteEndpointsLogCategory> logger,
                CancellationToken cancellationToken) =>
            {
                var validationProblem = ValidationExtensions.Validate(request);

                if (validationProblem is not null)
                {
                    return validationProblem;
                }

                CreateQuoteResult created;

                try
                {
                    created = await mediator.Send(
                        new CreateQuoteCommand(
                            request.Author,
                            request.Text),
                        cancellationToken);
                }
                catch (ArgumentException ex)
                {
                    logger.LogWarning(
                        "Quote creation rejected: {Reason}",
                        ex.Message);

                    return Results.BadRequest(
                        new ProblemDetails
                        {
                            Title = "Quote validation failed",
                            Detail = ex.Message
                        });
                }

                logger.LogInformation(
                    "Created quote {QuoteId}",
                    created.Id);

                return Results.Created(
                    $"/api/quotes/{created.Id}",
                    created);
            })
            .RequireAuthorization(PermissionClaims.CanEditQuotes);

        group.MapGet(
            "/{id:int}",
            async (
                int id,
                IMediator mediator,
                ILogger<QuoteEndpointsLogCategory> logger,
                CancellationToken cancellationToken) =>
            {
                var quote = await mediator.Send(
                    new GetQuoteByIdQuery(id),
                    cancellationToken);

                if (quote is null)
                {
                    logger.LogWarning(
                        "Quote {QuoteId} not found",
                        id);

                    return Results.NotFound(
                        new ProblemDetails
                        {
                            Title = "Quote not found",
                            Detail =
                                $"No quote exists with ID {id}."
                        });
                }

                return Results.Ok(quote);
            });

        group.MapDelete(
            "/{id:int}",
            async (
                int id,
                IQuoteRepository repository,
                ILogger<QuoteEndpointsLogCategory> logger,
                CancellationToken cancellationToken) =>
            {
                var deleted = await repository.DeleteAsync(
                    id,
                    cancellationToken);

                if (!deleted)
                {
                    logger.LogWarning(
                        "Delete failed, quote {QuoteId} not found",
                        id);

                    return Results.NotFound(
                        new ProblemDetails
                        {
                            Title = "Quote not found",
                            Detail =
                                $"No quote exists with ID {id}."
                        });
                }

                logger.LogInformation(
                    "Deleted quote {QuoteId}",
                    id);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionClaims.CanEditQuotes);
    }
}
