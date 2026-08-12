using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuotesApi.Authorization;
using QuotesApi.Services;

namespace QuotesApi.Controllers;

[ApiController]
[Route("api/collections")]
public class CollectionsController : ControllerBase
{
    private readonly ICollectionService _service;
    private readonly IAuthorizationService _authorizationService;

    public CollectionsController(
        ICollectionService service,
        IAuthorizationService authorizationService)
    {
        _service = service;
        _authorizationService = authorizationService;
    }

    [HttpPost]
    [Authorize(Policy = PermissionClaims.CanEditQuotes)]
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
    [Authorize(Policy = PermissionClaims.CanEditQuotes)]
    public async Task<IActionResult> AddQuote(
        int id,
        [FromBody] AddQuoteRequest request,
        CancellationToken cancellationToken)
    {
        var existingCollection = await _service.GetByIdAsync(
            id,
            cancellationToken);

        if (existingCollection is null)
        {
            return NotFound();
        }

        var authorizationResult = await _authorizationService.AuthorizeAsync(
            User,
            existingCollection,
            new CollectionOwnershipRequirement());

        if (!authorizationResult.Succeeded)
        {
            return Forbid();
        }

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
    [Authorize(Policy = PermissionClaims.CanEditQuotes)]
    public async Task<IActionResult> RemoveQuote(
        int id,
        int quoteId,
        CancellationToken cancellationToken)
    {
        var existingCollection = await _service.GetByIdAsync(
            id,
            cancellationToken);

        if (existingCollection is null)
        {
            return NotFound();
        }

        var authorizationResult = await _authorizationService.AuthorizeAsync(
            User,
            existingCollection,
            new CollectionOwnershipRequirement());

        if (!authorizationResult.Succeeded)
        {
            return Forbid();
        }

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