using System.ComponentModel.DataAnnotations;

namespace QuotesApi.DTOs;

public record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(8, ErrorMessage = "Password must be at least 8 characters.")] string Password);
