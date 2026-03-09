using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Features.Auth
{
    public record RegisterRequest(
    string Username,
    string Email,
    string Password,
    string? FirstName,
    string? LastName,
    string UserType  // "Internal" o "External"
);

    public record LoginRequest(
        string Email,
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
