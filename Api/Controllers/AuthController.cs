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
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
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

            return Ok(result);
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

            return Ok(result);
        }

        [HttpPost("refresh")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var result = await _authService.RefreshAsync(request.RefreshToken);
            if (result is null)
                return Unauthorized(new { message = "Refresh token non valido o scaduto." });

            return Ok(result);
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

            // Risponde sempre 204 per anti-enumeration (non riveliamo se l'email esiste)
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
        public async Task<IActionResult> Logout([FromBody] RefreshRequest request)
        {
            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

            await _authService.RevokeAsync(userId, request.RefreshToken);
            return NoContent();
        }
    }

    public record ForgotPasswordRequest([Required, EmailAddress] string Email);

    public record ResetPasswordRequest(
        [Required] int UserId,
        [Required] string Token,
        [Required, MinLength(8), MaxLength(100)] string NewPassword);
}
