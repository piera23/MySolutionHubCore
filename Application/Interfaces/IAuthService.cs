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
    }
}
