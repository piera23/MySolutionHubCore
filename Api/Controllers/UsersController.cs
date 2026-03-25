using Application.Common;
using Application.Interfaces;
using Asp.Versioning;
using Domain.Interfaces;
using Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace Api.Controllers
{
    [Authorize]
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IJwtService _jwtService;
        private readonly IAuditService _audit;
        private readonly IAuthService _authService;

        public UsersController(
            UserManager<ApplicationUser> userManager,
            IJwtService jwtService,
            IAuditService audit,
            IAuthService authService)
        {
            _userManager = userManager;
            _jwtService = jwtService;
            _audit = audit;
            _authService = authService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            pageSize = Math.Clamp(pageSize, 1, 100);
            page = Math.Max(page, 1);

            var query = _userManager.Users;
            var total = await query.CountAsync();

            var items = await query
                .OrderBy(u => u.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new
                {
                    u.Id,
                    u.UserName,
                    u.Email,
                    u.FirstName,
                    u.LastName,
                    u.PhoneNumber,
                    u.AvatarUrl,
                    u.IsActive,
                    u.IsDeleted,
                    u.UserType,
                    u.LastLoginAt,
                    u.CreatedAt,
                    u.UpdatedAt
                })
                .ToListAsync();

            return Ok(new PaginatedResult<object>(items, total, page, pageSize));
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user is null) return NotFound();

            return Ok(new
            {
                user.Id,
                user.UserName,
                user.Email,
                user.FirstName,
                user.LastName,
                user.PhoneNumber,
                user.AvatarUrl,
                user.IsActive,
                user.IsDeleted,
                user.UserType,
                user.LastLoginAt,
                user.CreatedAt,
                user.UpdatedAt
            });
        }

        [HttpGet("{id}/token")]
        public async Task<IActionResult> GetToken(int id)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user is null) return NotFound();

            var roles = await _userManager.GetRolesAsync(user);
            var token = _jwtService.GenerateToken(user, roles);

            return Ok(new
            {
                user.Id,
                user.UserName,
                user.Email,
                Token = token,
                ExpiresIn = "8 ore"
            });
        }

        [HttpGet("me")]
        public async Task<IActionResult> GetMe()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var user = await _userManager.FindByIdAsync(userId ?? "");
            if (user is null) return NotFound();

            return Ok(new
            {
                user.Id,
                user.UserName,
                user.Email,
                user.FirstName,
                user.LastName,
                user.PhoneNumber,
                user.AvatarUrl,
                user.IsActive,
                user.UserType,
                user.LastLoginAt,
                user.CreatedAt,
                user.UpdatedAt
            });
        }

        [HttpPut("me")]
        public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileRequest request)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var user = await _userManager.FindByIdAsync(userId ?? "");
            if (user is null) return NotFound();

            user.FirstName = request.FirstName;
            user.LastName = request.LastName;
            user.PhoneNumber = request.PhoneNumber;
            user.AvatarUrl = request.AvatarUrl;
            user.UpdatedAt = DateTime.UtcNow;

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
                return BadRequest(result.Errors.Select(e => e.Description));

            return Ok(new
            {
                user.Id,
                user.UserName,
                user.Email,
                user.FirstName,
                user.LastName,
                user.PhoneNumber,
                user.AvatarUrl,
                user.UserType,
                user.UpdatedAt
            });
        }

        [HttpDelete("me")]
        public async Task<IActionResult> DeleteMe(CancellationToken ct)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var success = await _authService.DeleteAccountAsync(userId, ct);
            if (!success) return NotFound();

            Response.Cookies.Delete("refresh_token", new CookieOptions { Path = "/api/" });
            return NoContent();
        }

        [HttpPut("me/password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var user = await _userManager.FindByIdAsync(userId ?? "");
            if (user is null) return NotFound();

            var result = await _userManager.ChangePasswordAsync(
                user, request.CurrentPassword, request.NewPassword);

            if (!result.Succeeded)
                return BadRequest(result.Errors.Select(e => e.Description));

            return NoContent();
        }

        [HttpPost("{id}/make-admin")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> MakeAdmin(int id)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user is null) return NotFound();

            if (!await _userManager.IsInRoleAsync(user, "Admin"))
                await _userManager.AddToRoleAsync(user, "Admin");

            var roles = await _userManager.GetRolesAsync(user);
            var token = _jwtService.GenerateToken(user, roles);

            await _audit.LogAsync("MakeAdmin",
                entityType: "User",
                entityId: id.ToString(),
                changes: $"User '{user.UserName}' promoted to Admin");

            return Ok(new { user.UserName, Roles = roles, Token = token });
        }
    }

    public record UpdateProfileRequest(
        [MaxLength(100)] string FirstName,
        [MaxLength(100)] string LastName,
        [Phone, MaxLength(20)] string? PhoneNumber,
        [Url, MaxLength(500)] string? AvatarUrl);

    public record ChangePasswordRequest(
        [Required] string CurrentPassword,
        [Required, MinLength(8), MaxLength(100)] string NewPassword);

}