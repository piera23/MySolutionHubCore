using FluentAssertions;
using Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Tests;

public class LoggingEmailServiceTests
{
    [Fact]
    public async Task SendAsync_ReturnsCompletedTask()
    {
        var logger = new Mock<ILogger<LoggingEmailService>>();
        var svc = new LoggingEmailService(logger.Object);

        var task = svc.SendAsync("user@example.com", "Test", "<p>body</p>");

        await task; // non deve lanciare
        task.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_LogsToAddress()
    {
        var logger = new Mock<ILogger<LoggingEmailService>>();
        var svc = new LoggingEmailService(logger.Object);

        await svc.SendAsync("dest@example.com", "Oggetto", "<p>corpo</p>");

        logger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString()!.Contains("dest@example.com")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendAsync_LogsSubject()
    {
        var logger = new Mock<ILogger<LoggingEmailService>>();
        var svc = new LoggingEmailService(logger.Object);

        await svc.SendAsync("x@x.com", "Password dimenticata", "<p>link</p>");

        logger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString()!.Contains("Password dimenticata")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendAsync_WorksWithCancellationToken()
    {
        var logger = new Mock<ILogger<LoggingEmailService>>();
        var svc = new LoggingEmailService(logger.Object);
        using var cts = new CancellationTokenSource();

        var act = async () => await svc.SendAsync("a@b.com", "Sub", "body", cts.Token);

        await act.Should().NotThrowAsync();
    }
}
