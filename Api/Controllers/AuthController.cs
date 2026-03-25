using Application.Features.Auth;
using Application.Interfaces;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace Api.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    public class AuthController : ControllerBase
    {
        private const string RefreshTokenCookie = "refresh_token";
        private static readonly TimeSpan RefreshTokenTtl = TimeSpan.FromDays(7);

        private readonly IAuthService _authService;
        private readonly IWebHostEnvironment _env;

        public AuthController(IAuthService authService, IWebHostEnvironment env)
        {
            _authService = authService;
            _env = env;
        }

        [HttpPost("register")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var result = await _authService.RegisterAsync(request);
            if (result is null)
                return BadRequest(new { message = "Registrazione fallita. Email già in uso o dati non validi." });

            SetRefreshCookie(result.RefreshToken);
            return Ok(ToTokenResponse(result));
        }

        [HttpPost("login")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var result = await _authService.LoginAsync(request);
            if (result is null)
                return Unauthorized(new { message = "Credenziali non valide." });

            SetRefreshCookie(result.RefreshToken);
            return Ok(ToTokenResponse(result));
        }

        /// <summary>
        /// Rinnova il JWT usando il refresh token dal cookie HttpOnly.
        /// Accetta anche il token nel body per compatibilità con client non-browser.
        /// </summary>
        [HttpPost("refresh")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest? request = null)
        {
            // Priorità: cookie HttpOnly → body (backward compat)
            var refreshToken = Request.Cookies[RefreshTokenCookie]
                               ?? request?.RefreshToken;

            if (string.IsNullOrEmpty(refreshToken))
                return Unauthorized(new { message = "Refresh token mancante." });

            var result = await _authService.RefreshAsync(refreshToken);
            if (result is null)
            {
                ClearRefreshCookie();
                return Unauthorized(new { message = "Refresh token non valido o scaduto." });
            }

            SetRefreshCookie(result.RefreshToken);
            return Ok(ToTokenResponse(result));
        }

        [HttpGet("confirm-email")]
        public async Task<IActionResult> ConfirmEmail([FromQuery] int userId, [FromQuery] string token)
        {
            if (userId <= 0 || string.IsNullOrWhiteSpace(token))
                return BadRequest(new { message = "Parametri non validi." });

            var success = await _authService.ConfirmEmailAsync(userId, token);
            if (!success)
                return BadRequest(new { message = "Link di conferma non valido o scaduto." });

            return Ok(new { message = "Email confermata con successo. Puoi ora accedere." });
        }

        [HttpPost("forgot-password")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            await _authService.ForgotPasswordAsync(request.Email);
            return NoContent();
        }

        [HttpPost("reset-password")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var success = await _authService.ResetPasswordAsync(
                request.UserId, request.Token, request.NewPassword);

            if (!success)
                return BadRequest(new { message = "Link non valido o scaduto. Richiedi un nuovo reset." });

            return NoContent();
        }

        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] RefreshRequest? request = null)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

            var refreshToken = Request.Cookies[RefreshTokenCookie] ?? request?.RefreshToken;
            if (!string.IsNullOrEmpty(refreshToken))
                await _authService.RevokeAsync(userId, refreshToken);

            ClearRefreshCookie();
            return NoContent();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void SetRefreshCookie(string token)
        {
            Response.Cookies.Append(RefreshTokenCookie, token, new CookieOptions
            {
                HttpOnly  = true,
                Secure    = !_env.IsDevelopment(),
                SameSite  = SameSiteMode.Strict,
                Path      = "/api/",
                MaxAge    = RefreshTokenTtl
            });
        }

        private void ClearRefreshCookie()
        {
            Response.Cookies.Delete(RefreshTokenCookie, new CookieOptions
            {
                HttpOnly = true,
                Secure   = !_env.IsDevelopment(),
                SameSite = SameSiteMode.Strict,
                Path     = "/api/"
            });
        }

        private static TokenResponse ToTokenResponse(AuthResponse r) => new(
            AccessToken: r.Token,
            Username:    r.Username,
            Email:       r.Email,
            UserType:    r.UserType,
            ExpiresAt:   r.ExpiresAt,
            ExpiresIn:   (int)(r.ExpiresAt - DateTime.UtcNow).TotalSeconds
        );
    }

    public record ForgotPasswordRequest([Required, EmailAddress] string Email);

    public record ResetPasswordRequest(
        [Required] int UserId,
        [Required] string Token,
        [Required, MinLength(8), MaxLength(100)] string NewPassword);
}
