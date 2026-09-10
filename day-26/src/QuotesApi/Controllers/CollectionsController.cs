using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuotesApi.Authorization;
using QuotesApi.DTOs;
using QuotesApi.Services;

namespace QuotesApi.Controllers;

[ApiController]
[Route("api/collections")]
public class CollectionsController : ControllerBase
{
    private readonly ICollectionService _service;
    private readonly IAuthorizationService _authorizationService;
    private readonly ILogger<CollectionsController> _logger;

    public CollectionsController(
        ICollectionService service,
        IAuthorizationService authorizationService,
        ILogger<CollectionsController> logger)
    {
        _service = service;
        _authorizationService = authorizationService;
        _logger = logger;
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

            _logger.LogInformation(
                "Created collection {CollectionId} for owner {OwnerId}",
                collection.Id,
                collection.OwnerId);

            return CreatedAtAction(
                nameof(GetCollection),
                new { id = collection.Id },
                collection);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(
                "Collection creation rejected: {Reason}",
                ex.Message);

            return Problem(
                title: "Collection validation failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
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
        [FromBody] AddCollectionItemRequest request,
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

            _logger.LogInformation(
                "Added quote {QuoteId} to collection {CollectionId}",
                request.QuoteId,
                id);

            return Ok(collection);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return Problem(
                title: "Collection validation failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                "Add quote to collection {CollectionId} rejected: {Reason}",
                id,
                ex.Message);

            return Problem(
                title: "Collection invariant violated",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
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

            _logger.LogInformation(
                "Removed quote {QuoteId} from collection {CollectionId}",
                quoteId,
                id);

            return Ok(collection);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                "Remove quote from collection {CollectionId} rejected: {Reason}",
                id,
                ex.Message);

            return Problem(
                title: "Collection invariant violated",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    }
}