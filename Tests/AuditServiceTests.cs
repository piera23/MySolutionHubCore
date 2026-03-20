using FluentAssertions;
using Infrastructure.Services;
using MasterDb.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Net;
using System.Security.Claims;

namespace Tests;

public class AuditServiceTests
{
    private static MasterDbContext CreateMasterDb(string dbName)
    {
        var opts = new DbContextOptionsBuilder<MasterDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new MasterDbContext(opts);
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LogAsync_SavesAuditLog_WithCorrectFields()
    {
        await using var db = CreateMasterDb(nameof(LogAsync_SavesAuditLog_WithCorrectFields));

        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "42"),
            new Claim(ClaimTypes.Name, "Mario Rossi")
        ]));

        var mockHttp = new Mock<IHttpContextAccessor>();
        var httpCtx = new DefaultHttpContext { User = user };
        httpCtx.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.1");
        mockHttp.Setup(h => h.HttpContext).Returns(httpCtx);

        var svc = new AuditService(db, mockHttp.Object);

        await svc.LogAsync(
            action: "user:login",
            tenantId: "t1",
            entityType: "User",
            entityId: "42",
            changes: "{\"loginAt\":\"2024-01-01\"}");

        var log = await db.AuditLogs.FirstAsync();
        log.Action.Should().Be("user:login");
        log.TenantId.Should().Be("t1");
        log.ActorId.Should().Be("42");
        log.ActorName.Should().Be("Mario Rossi");
        log.EntityType.Should().Be("User");
        log.EntityId.Should().Be("42");
        log.Changes.Should().Contain("loginAt");
        log.IpAddress.Should().Be("192.168.1.1");
        log.Timestamp.Should().BeCloseTo(DateTime.UtcNow, precision: TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task LogAsync_UsesSystemActor_WhenNoHttpContext()
    {
        await using var db = CreateMasterDb(nameof(LogAsync_UsesSystemActor_WhenNoHttpContext));

        var mockHttp = new Mock<IHttpContextAccessor>();
        mockHttp.Setup(h => h.HttpContext).Returns((HttpContext?)null);

        var svc = new AuditService(db, mockHttp.Object);

        await svc.LogAsync("tenant:provision", tenantId: "t2");

        var log = await db.AuditLogs.FirstAsync();
        log.ActorId.Should().Be("system");
        log.ActorName.Should().Be("system");
        log.IpAddress.Should().BeNull();
    }

    [Fact]
    public async Task LogAsync_UsesNameClaim_AsActorName_WhenNameIdentifierAbsent()
    {
        await using var db =
            CreateMasterDb(nameof(LogAsync_UsesNameClaim_AsActorName_WhenNameIdentifierAbsent));

        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "99"),
            new Claim("name", "Giulia Bianchi")
        ]));

        var mockHttp = new Mock<IHttpContextAccessor>();
        var httpCtx = new DefaultHttpContext { User = user };
        mockHttp.Setup(h => h.HttpContext).Returns(httpCtx);

        var svc = new AuditService(db, mockHttp.Object);

        await svc.LogAsync("test:action");

        var log = await db.AuditLogs.FirstAsync();
        log.ActorName.Should().Be("Giulia Bianchi");
    }

    [Fact]
    public async Task LogAsync_SavesMultipleLogs_Independently()
    {
        await using var db = CreateMasterDb(nameof(LogAsync_SavesMultipleLogs_Independently));

        var mockHttp = new Mock<IHttpContextAccessor>();
        mockHttp.Setup(h => h.HttpContext).Returns((HttpContext?)null);

        var svc = new AuditService(db, mockHttp.Object);

        await svc.LogAsync("action1");
        await svc.LogAsync("action2");
        await svc.LogAsync("action3");

        var logs = await db.AuditLogs.ToListAsync();
        logs.Should().HaveCount(3);
        logs.Select(l => l.Action).Should().Contain(["action1", "action2", "action3"]);
    }
}
