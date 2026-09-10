using System.ComponentModel.DataAnnotations;

namespace QuotesApi.DTOs;

public record RefreshTokenRequest(
    [Required] string RefreshToken);
