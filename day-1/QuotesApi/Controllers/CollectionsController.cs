using Microsoft.AspNetCore.Mvc;
using QuotesApi.DTOs;
using QuotesApi.Models;
using QuotesApi.Repositories;
using QuotesApi.Services;

namespace QuotesApi.Controllers;

[ApiController]
[Route("api/collections")]
public class CollectionsController : ControllerBase
{
    private readonly ICollectionRepository _repository;
    private readonly IClock _clock;

    public CollectionsController(
        ICollectionRepository repository,
        IClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    // ==========================================
    // POST /api/collections
    // Create a new collection
    // ==========================================

    [HttpPost]
    public async Task<IActionResult> CreateCollection(
        [FromBody] CreateCollectionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var collection = new Collection(
                request.Name,
                request.OwnerId);

            await _repository.AddAsync(
                collection,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetCollection),
                new { id = collection.Id },
                collection);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
    }

    // ==========================================
    // GET /api/collections/{id}
    // Get a collection
    // ==========================================

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetCollection(
        int id,
        CancellationToken cancellationToken)
    {
        var collection = await _repository.GetByIdAsync(
            id,
            cancellationToken);

        if (collection is null)
        {
            return NotFound(new
            {
                error = $"Collection with ID {id} was not found."
            });
        }

        return Ok(collection);
    }

    // ==========================================
    // POST /api/collections/{id}/items
    // Add a quote to a collection
    // ==========================================

    [HttpPost("{id:int}/items")]
    public async Task<IActionResult> AddQuote(
        int id,
        [FromBody] AddCollectionItemRequest request,
        CancellationToken cancellationToken)
    {
        var collection = await _repository.GetByIdAsync(
            id,
            cancellationToken);

        if (collection is null)
        {
            return NotFound(new
            {
                error = $"Collection with ID {id} was not found."
            });
        }

        try
        {
            // IMPORTANT:
            // The controller does NOT directly add anything
            // to the database.
            //
            // The aggregate root controls the mutation.
            collection.AddItem(
                request.QuoteId,
                _clock.UtcNow);

            await _repository.UpdateAsync(
                collection,
                cancellationToken);

            return Ok(collection);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
    }

    // ==========================================
    // DELETE /api/collections/{id}/items/{quoteId}
    // Remove a quote from a collection
    // ==========================================

    [HttpDelete("{id:int}/items/{quoteId:int}")]
    public async Task<IActionResult> RemoveQuote(
        int id,
        int quoteId,
        CancellationToken cancellationToken)
    {
        var collection = await _repository.GetByIdAsync(
            id,
            cancellationToken);

        if (collection is null)
        {
            return NotFound(new
            {
                error = $"Collection with ID {id} was not found."
            });
        }

        try
        {
            // Again, mutation goes through the aggregate.
            collection.RemoveItem(quoteId);

            await _repository.UpdateAsync(
                collection,
                cancellationToken);

            return Ok(collection);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
    }
}