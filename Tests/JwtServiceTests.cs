using FluentAssertions;
using Infrastructure.Identity;
using Infrastructure.MultiTenant;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Tests;

public class JwtServiceTests
{
    private static IConfiguration CreateConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "chiave-segreta-jwt-test-minimo-32-caratteri!!",
                ["Jwt:Issuer"] = "TestIssuer",
                ["Jwt:Audience"] = "TestAudience"
            })
            .Build();

    private static TenantContext CreateTenantContext(string tenantId = "tenant1")
    {
        var ctx = new TenantContext();
        ctx.SetTenant(tenantId, "Test Tenant", "Host=postgres;Port=5432;Database=mysolutionhub_test;Username=postgres;Password=postgres_dev", []);
        return ctx;
    }

    private static ApplicationUser CreateUser(int id = 1) => new()
    {
        Id = id,
        UserName = "mario.rossi",
        Email = "mario@example.com",
        UserType = UserType.Internal,
        IsActive = true
    };

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateToken_ReturnsNonEmptyString()
    {
        var svc = new JwtService(CreateConfig(), CreateTenantContext());

        var token = svc.GenerateToken(CreateUser(), []);

        token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GenerateToken_ContainsCorrectUserClaims()
    {
        var user = CreateUser(42);
        var svc = new JwtService(CreateConfig(), CreateTenantContext("t42"));

        var tokenString = svc.GenerateToken(user, []);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenString);

        jwt.Claims.Should().Contain(c =>
            c.Type == ClaimTypes.NameIdentifier && c.Value == "42");
        jwt.Claims.Should().Contain(c =>
            c.Type == ClaimTypes.Email && c.Value == "mario@example.com");
        jwt.Claims.Should().Contain(c =>
            c.Type == ClaimTypes.Name && c.Value == "mario.rossi");
        jwt.Claims.Should().Contain(c =>
            c.Type == "tenantId" && c.Value == "t42");
    }

    [Fact]
    public void GenerateToken_IncludesRoles_InClaims()
    {
        var svc = new JwtService(CreateConfig(), CreateTenantContext());

        var tokenString = svc.GenerateToken(CreateUser(), ["Admin", "Manager"]);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenString);

        jwt.Claims.Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .Should().Contain(["Admin", "Manager"]);
    }

    [Fact]
    public void GenerateToken_UsesConfiguredIssuerAndAudience()
    {
        var svc = new JwtService(CreateConfig(), CreateTenantContext());

        var tokenString = svc.GenerateToken(CreateUser(), []);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenString);

        jwt.Issuer.Should().Be("TestIssuer");
        jwt.Audiences.Should().Contain("TestAudience");
    }

    [Fact]
    public void GenerateToken_TokenExpiresInAbout8Hours()
    {
        var svc = new JwtService(CreateConfig(), CreateTenantContext());

        var tokenString = svc.GenerateToken(CreateUser(), []);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenString);

        var remaining = jwt.ValidTo - DateTime.UtcNow;
        remaining.Should().BeCloseTo(TimeSpan.FromHours(8), precision: TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void GenerateToken_IsSignedWithHs256()
    {
        var svc = new JwtService(CreateConfig(), CreateTenantContext());

        var tokenString = svc.GenerateToken(CreateUser(), []);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenString);

        jwt.Header.Alg.Should().Be(SecurityAlgorithms.HmacSha256);
    }

    [Fact]
    public void GenerateToken_CanBeValidated_WithSameKey()
    {
        var config = CreateConfig();
        var svc = new JwtService(config, CreateTenantContext());
        var tokenString = svc.GenerateToken(CreateUser(), []);

        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(config["Jwt:Key"]!));

        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "TestIssuer",
            ValidateAudience = true,
            ValidAudience = "TestAudience",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        var act = () => handler.ValidateToken(tokenString, validationParams, out _);
        act.Should().NotThrow();
    }

    [Fact]
    public void GenerateToken_Throws_WhenJwtKeyIsMissing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "TestIssuer",
                ["Jwt:Audience"] = "TestAudience"
                // Jwt:Key deliberately omitted
            })
            .Build();

        var svc = new JwtService(config, CreateTenantContext());

        var act = () => svc.GenerateToken(CreateUser(), []);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Jwt:Key*");
    }
}
