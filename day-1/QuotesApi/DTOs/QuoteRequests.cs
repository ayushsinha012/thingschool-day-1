namespace QuotesApi.DTOs;

public record CreateQuoteRequest(
    string Author,
    string Text
);