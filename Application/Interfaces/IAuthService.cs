using Application.Features.Auth;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces
{
    public interface IAuthService
    {
        Task<AuthResponse?> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
        Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default);

        /// <summary>
        /// Valida il refresh token, genera un nuovo JWT e ruota il refresh token.
        /// Restituisce null se il token è scaduto, revocato o inesistente.
        /// </summary>
        Task<AuthResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default);

        /// <summary>Revoca il refresh token specificato (logout).</summary>
        Task RevokeAsync(int userId, string refreshToken, CancellationToken ct = default);

        /// <summary>Conferma l'email tramite il token generato da Identity.</summary>
        Task<bool> ConfirmEmailAsync(int userId, string token, CancellationToken ct = default);
    }
}
