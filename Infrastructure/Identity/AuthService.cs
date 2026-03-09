using Application.Features.Auth;
using Application.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Identity
{
    public class AuthService : IAuthService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IJwtService _jwtService;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            UserManager<ApplicationUser> userManager,
            IJwtService jwtService,
            ILogger<AuthService> logger)
        {
            _userManager = userManager;
            _jwtService = jwtService;
            _logger = logger;
        }

        public async Task<AuthResponse?> RegisterAsync(
            RegisterRequest request,
            CancellationToken ct = default)
        {
            // Verifica se l'email esiste già
            var existing = await _userManager.FindByEmailAsync(request.Email);
            if (existing is not null)
            {
                _logger.LogWarning("Tentativo di registrazione con email già esistente: {Email}", request.Email);
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

            // Assegna ruolo base
            var role = userType == UserType.Internal ? "Internal" : "External";
            await _userManager.AddToRoleAsync(user, role);

            var roles = await _userManager.GetRolesAsync(user);
            var token = _jwtService.GenerateToken(user, roles);

            return new AuthResponse(
                Token: token,
                Username: user.UserName!,
                Email: user.Email!,
                UserType: user.UserType.ToString(),
                ExpiresAt: DateTime.UtcNow.AddHours(8)
            );
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

            // Aggiorna LastLoginAt
            user.LastLoginAt = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);

            var roles = await _userManager.GetRolesAsync(user);
            var token = _jwtService.GenerateToken(user, roles);

            return new AuthResponse(
                Token: token,
                Username: user.UserName!,
                Email: user.Email!,
                UserType: user.UserType.ToString(),
                ExpiresAt: DateTime.UtcNow.AddHours(8)
            );
        }
    }
}
