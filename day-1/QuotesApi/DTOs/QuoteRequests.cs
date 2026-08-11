namespace QuotesApi.DTOs;

// ==========================================
// Task 3 - Quote DTO
// ==========================================

public record CreateQuoteRequest(
    string Author,
    string Text
);

// ==========================================
// Task 7 - Collection DTOs
// ==========================================

public record CreateCollectionRequest(
    string Name,
    int OwnerId
);

public record AddCollectionItemRequest(
    int QuoteId
);