using System.ComponentModel.DataAnnotations;

namespace QuotesApi.DTOs;

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);
