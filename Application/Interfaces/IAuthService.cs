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

        /// <summary>
        /// Genera un token di reset password e invia l'email.
        /// Non rivela se l'email esiste (anti-enumeration).
        /// </summary>
        Task ForgotPasswordAsync(string email, CancellationToken ct = default);

        /// <summary>Valida il token e imposta la nuova password.</summary>
        Task<bool> ResetPasswordAsync(int userId, string token, string newPassword, CancellationToken ct = default);

        /// <summary>
        /// GDPR right-to-erasure: anonimizza tutti i dati personali dell'utente e
        /// revoca tutti i suoi refresh token. Non elimina fisicamente i record per
        /// preservare l'integrità referenziale (messaggi, attività, ecc.).
        /// </summary>
        Task<bool> DeleteAccountAsync(int userId, CancellationToken ct = default);
    }
}
