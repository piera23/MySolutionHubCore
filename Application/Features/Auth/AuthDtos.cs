using System.ComponentModel.DataAnnotations;

namespace Application.Features.Auth
{
    public record RegisterRequest(
        [Required, MinLength(3), MaxLength(50)]
        string Username,

        [Required, EmailAddress, MaxLength(256)]
        string Email,

        [Required, MinLength(8), MaxLength(100)]
        string Password,

        [MaxLength(100)]
        string? FirstName,

        [MaxLength(100)]
        string? LastName,

        [Required, RegularExpression("^(Internal|External)$",
            ErrorMessage = "UserType deve essere 'Internal' o 'External'.")]
        string UserType
    );

    public record LoginRequest(
        [Required, EmailAddress, MaxLength(256)]
        string Email,

        [Required, MinLength(1)]
        string Password
    );

    public record AuthResponse(
        string Token,
        string Username,
        string Email,
        string UserType,
        DateTime ExpiresAt
    );
}
