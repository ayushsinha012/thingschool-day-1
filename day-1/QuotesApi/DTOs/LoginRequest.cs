namespace QuotesApi.DTOs;

public record LoginRequest(
    string Email,
    string Password);
