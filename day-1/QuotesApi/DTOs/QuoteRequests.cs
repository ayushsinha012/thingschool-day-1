using System.ComponentModel.DataAnnotations;

namespace QuotesApi.DTOs;

// ==========================================
// Task 3 - Quote DTO
// ==========================================

public record CreateQuoteRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Author,
    [Required, StringLength(1000, MinimumLength = 1)] string Text
);

// ==========================================
// Task 7 - Collection DTOs
// ==========================================

public record CreateCollectionRequest(
    [Required, StringLength(80, MinimumLength = 3)] string Name,
    [Range(1, int.MaxValue)] int OwnerId
);

public record AddCollectionItemRequest(
    [Range(1, int.MaxValue)] int QuoteId
);