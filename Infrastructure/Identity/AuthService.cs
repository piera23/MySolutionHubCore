using Application.Features.Auth;
using Application.Interfaces;
using Domain.Entities;
using Domain.Interfaces;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Identity
{
    public class AuthService : IAuthService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IJwtService _jwtService;
        private readonly BaseAppDbContext _db;
        private readonly IEmailService _email;
        private readonly IConfiguration _config;
        private readonly ILogger<AuthService> _logger;

        // Durata del refresh token: 7 giorni
        private static readonly TimeSpan RefreshTokenTtl = TimeSpan.FromDays(7);

        public AuthService(
            UserManager<ApplicationUser> userManager,
            IJwtService jwtService,
            BaseAppDbContext db,
            IEmailService email,
            IConfiguration config,
            ILogger<AuthService> logger)
        {
            _userManager = userManager;
            _jwtService = jwtService;
            _db = db;
            _email = email;
            _config = config;
            _logger = logger;
        }

        public async Task<AuthResponse?> RegisterAsync(
            RegisterRequest request,
            CancellationToken ct = default)
        {
            var existing = await _userManager.FindByEmailAsync(request.Email);
            if (existing is not null)
            {
                _logger.LogWarning("Registrazione fallita — email già in uso: {Email}", request.Email);
                return null;
            }

            var userType = Enum.TryParse<UserType>(request.UserType, out var parsed)
                ? parsed
                : UserType.External;

            var user = new ApplicationUser
            {
                UserName = request.Username,
                Email = request.Email,
                FirstName = request.FirstName,
                LastName = request.LastName,
                UserType = userType,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                _logger.LogWarning("Registrazione fallita per {Email}: {Errors}",
                    request.Email,
                    string.Join(", ", result.Errors.Select(e => e.Description)));
                return null;
            }

            var role = userType == UserType.Internal ? "Internal" : "External";
            await _userManager.AddToRoleAsync(user, role);

            // Invia email di conferma
            await SendConfirmationEmailAsync(user, ct);

            return await BuildAuthResponseAsync(user, ct);
        }

        public async Task<AuthResponse?> LoginAsync(
            LoginRequest request,
            CancellationToken ct = default)
        {
            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user is null || !user.IsActive || user.IsDeleted)
            {
                _logger.LogWarning("Login fallito — utente non trovato: {Email}", request.Email);
                return null;
            }

            var passwordValid = await _userManager.CheckPasswordAsync(user, request.Password);
            if (!passwordValid)
            {
                _logger.LogWarning("Login fallito — password errata: {Email}", request.Email);
                return null;
            }

            user.LastLoginAt = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);

            return await BuildAuthResponseAsync(user, ct);
        }

        public async Task<AuthResponse?> RefreshAsync(
            string refreshToken,
            CancellationToken ct = default)
        {
            var stored = await _db.RefreshTokens
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.Token == refreshToken, ct);

            if (stored is null)
            {
                _logger.LogWarning("Refresh token non trovato: {Token}", refreshToken[..8]);
                return null;
            }

            if (stored.IsRevoked)
            {
                // Possibile riuso di token rubato: revoca tutta la catena
                _logger.LogWarning(
                    "Refresh token già revocato per utente {UserId}. Possibile riuso — revoca catena.",
                    stored.UserId);
                await RevokeAllForUserAsync(stored.UserId, ct);
                return null;
            }

            if (stored.ExpiresAt < DateTime.UtcNow)
            {
                _logger.LogWarning("Refresh token scaduto per utente {UserId}.", stored.UserId);
                return null;
            }

            var user = await _userManager.FindByIdAsync(stored.UserId.ToString());
            if (user is null || !user.IsActive || user.IsDeleted)
            {
                _logger.LogWarning("Utente non attivo per refresh token, UserId: {UserId}.", stored.UserId);
                return null;
            }

            // Token rotation: revoca il vecchio, crea il nuovo
            var newRefreshToken = GenerateToken();
            stored.IsRevoked = true;
            stored.ReplacedByToken = newRefreshToken;
            stored.UpdatedAt = DateTime.UtcNow;

            var response = await BuildAuthResponseAsync(user, ct, newRefreshToken);
            return response;
        }

        public async Task RevokeAsync(
            int userId,
            string refreshToken,
            CancellationToken ct = default)
        {
            var stored = await _db.RefreshTokens
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.Token == refreshToken && r.UserId == userId, ct);

            if (stored is null || stored.IsRevoked) return;

            stored.IsRevoked = true;
            stored.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Refresh token revocato per utente {UserId}.", userId);
        }

        // ── Helpers ───────────────────────────────────────────────

        private async Task<AuthResponse> BuildAuthResponseAsync(
            ApplicationUser user,
            CancellationToken ct,
            string? preGeneratedRefreshToken = null)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var jwt = _jwtService.GenerateToken(user, roles);

            var rtValue = preGeneratedRefreshToken ?? GenerateToken();
            _db.RefreshTokens.Add(new RefreshToken
            {
                UserId = user.Id,
                Token = rtValue,
                ExpiresAt = DateTime.UtcNow.Add(RefreshTokenTtl),
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);

            return new AuthResponse(
                Token: jwt,
                RefreshToken: rtValue,
                Username: user.UserName!,
                Email: user.Email!,
                UserType: user.UserType.ToString(),
                ExpiresAt: DateTime.UtcNow.AddHours(8)
            );
        }

        private async Task RevokeAllForUserAsync(int userId, CancellationToken ct)
        {
            var tokens = await _db.RefreshTokens
                .IgnoreQueryFilters()
                .Where(r => r.UserId == userId && !r.IsRevoked)
                .ToListAsync(ct);

            foreach (var t in tokens)
            {
                t.IsRevoked = true;
                t.UpdatedAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync(ct);
        }

        public async Task<bool> ConfirmEmailAsync(int userId, string token, CancellationToken ct = default)
        {
            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user is null)
            {
                _logger.LogWarning("ConfirmEmail: utente {UserId} non trovato.", userId);
                return false;
            }

            var result = await _userManager.ConfirmEmailAsync(user, token);
            if (!result.Succeeded)
            {
                _logger.LogWarning("ConfirmEmail fallito per {UserId}: {Errors}",
                    userId, string.Join(", ", result.Errors.Select(e => e.Description)));
                return false;
            }

            _logger.LogInformation("Email confermata per utente {UserId}.", userId);
            return true;
        }

        private async Task SendConfirmationEmailAsync(ApplicationUser user, CancellationToken ct)
        {
            try
            {
                var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                var encodedToken = Uri.EscapeDataString(token);
                var baseUrl = _config["App:BaseUrl"] ?? "http://localhost:5001";
                var link = $"{baseUrl}/confirm-email?userId={user.Id}&token={encodedToken}";

                var body = $"""
                    <p>Ciao {user.FirstName ?? user.UserName},</p>
                    <p>Conferma il tuo account cliccando sul link:</p>
                    <p><a href="{link}">{link}</a></p>
                    <p>Il link è valido per 24 ore.</p>
                    """;

                await _email.SendAsync(user.Email!, "Conferma il tuo account MySolutionHub", body, ct);
            }
            catch (Exception ex)
            {
                // Non bloccare la registrazione se l'email fallisce
                _logger.LogError(ex, "Errore invio email di conferma a {Email}.", user.Email);
            }
        }

        private static string GenerateToken() => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    }
}
