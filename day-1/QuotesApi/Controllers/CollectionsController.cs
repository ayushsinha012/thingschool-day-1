using Microsoft.AspNetCore.Mvc;
using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Controllers;

[ApiController]
[Route("api/collections")]
public class CollectionsController : ControllerBase
{
    private readonly ICollectionRepository _repository;

    public CollectionsController(
        ICollectionRepository repository)
    {
        _repository = repository;
    }

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
            return BadRequest(ex.Message);
        }
    }

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
            return NotFound();
        }

        return Ok(collection);
    }

    [HttpPost("{id:int}/items")]
    public async Task<IActionResult> AddQuote(
        int id,
        [FromBody] AddQuoteRequest request,
        CancellationToken cancellationToken)
    {
        var collection = await _repository.GetByIdAsync(
            id,
            cancellationToken);

        if (collection is null)
        {
            return NotFound();
        }

        try
        {
            collection.AddItem(request.QuoteId);

            await _repository.UpdateAsync(
                collection,
                cancellationToken);

            return Ok(collection);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

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
            return NotFound();
        }

        try
        {
            collection.RemoveItem(quoteId);

            await _repository.UpdateAsync(
                collection,
                cancellationToken);

            return Ok(collection);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}

public record CreateCollectionRequest(
    string Name,
    int OwnerId);

public record AddQuoteRequest(
    int QuoteId);