using Microsoft.AspNetCore.Mvc;
using QuotesApi.Services;

namespace QuotesApi.Controllers;

[ApiController]
[Route("api/collections")]
public class CollectionsController : ControllerBase
{
    private readonly ICollectionService _service;

    public CollectionsController(
        ICollectionService service)
    {
        _service = service;
    }

    [HttpPost]
    public async Task<IActionResult> CreateCollection(
        [FromBody] CreateCollectionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var collection = await _service.CreateAsync(
                request.Name,
                request.OwnerId,
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
        var collection = await _service.GetByIdAsync(
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
        try
        {
            var collection = await _service.AddQuoteAsync(
                id,
                request.QuoteId,
                cancellationToken);

            return Ok(collection);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
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
        try
        {
            var collection = await _service.RemoveQuoteAsync(
                id,
                quoteId,
                cancellationToken);

            return Ok(collection);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
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