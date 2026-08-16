using Microsoft.AspNetCore.Mvc;
using QuotesApi.Authorization;
using QuotesApi.DTOs;
using QuotesApi.Models;
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

        group.MapPost(
            "/",
            async (
                CreateQuoteRequest request,
                IQuoteRepository repository,
                ILogger<QuoteEndpointsLogCategory> logger,
                CancellationToken cancellationToken) =>
            {
                var validationProblem = ValidationExtensions.Validate(request);

                if (validationProblem is not null)
                {
                    return validationProblem;
                }

                Quote quote;

                try
                {
                    quote = Quote.Create(
                        request.Author,
                        request.Text);
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

                var created = await repository.AddAsync(
                    quote,
                    cancellationToken);

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
                IQuoteRepository repository,
                ILogger<QuoteEndpointsLogCategory> logger,
                CancellationToken cancellationToken) =>
            {
                var quote = await repository.GetByIdAsync(
                    id,
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
