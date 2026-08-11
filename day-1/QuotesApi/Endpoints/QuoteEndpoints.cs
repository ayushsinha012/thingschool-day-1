using Microsoft.AspNetCore.Mvc;
using QuotesApi.DTOs;
using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Endpoints;

public static class QuoteEndpoints
{
    public static void MapQuoteEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/quotes");

        // ==========================================
        // GET /api/quotes
        // ==========================================

        group.MapGet(
            "/",
            async (
                int? page,
                int? size,
                IQuoteRepository repository,
                CancellationToken cancellationToken) =>
            {
                var currentPage = page.GetValueOrDefault(1);
                var pageSize = size.GetValueOrDefault(10);

                if (currentPage < 1 ||
                    pageSize < 1 ||
                    pageSize > 100)
                {
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

                return Results.Ok(
                    new
                    {
                        page = currentPage,
                        size = pageSize,
                        total = result.Total,
                        items = result.Items
                    });
            });

        // ==========================================
        // POST /api/quotes
        // ==========================================

        group.MapPost(
            "/",
            async (
                CreateQuoteRequest request,
                IQuoteRepository repository,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.Author) ||
                    string.IsNullOrWhiteSpace(request.Text))
                {
                    return Results.BadRequest(
                        new ProblemDetails
                        {
                            Title = "Validation failed",
                            Detail = "Author and text are required."
                        });
                }

                var quote = new Quote
                {
                    Author = request.Author.Trim(),
                    Text = request.Text.Trim()
                };

                var created = await repository.AddAsync(
                    quote,
                    cancellationToken);

                return Results.Created(
                    $"/api/quotes/{created.Id}",
                    created);
            });

        // ==========================================
        // GET /api/quotes/{id}
        // ==========================================

        group.MapGet(
            "/{id:int}",
            async (
                int id,
                IQuoteRepository repository,
                CancellationToken cancellationToken) =>
            {
                var quote = await repository.GetByIdAsync(
                    id,
                    cancellationToken);

                return quote is null
                    ? Results.NotFound(
                        new ProblemDetails
                        {
                            Title = "Quote not found",
                            Detail =
                                $"No quote exists with ID {id}."
                        })
                    : Results.Ok(quote);
            });

        // ==========================================
        // DELETE /api/quotes/{id}
        // ==========================================

        group.MapDelete(
            "/{id:int}",
            async (
                int id,
                IQuoteRepository repository,
                CancellationToken cancellationToken) =>
            {
                var deleted = await repository.DeleteAsync(
                    id,
                    cancellationToken);

                return deleted
                    ? Results.NoContent()
                    : Results.NotFound(
                        new ProblemDetails
                        {
                            Title = "Quote not found",
                            Detail =
                                $"No quote exists with ID {id}."
                        });
            });
    }
}