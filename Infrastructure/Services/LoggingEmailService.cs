using Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
    /// <summary>
    /// Implementazione no-op per sviluppo: stampa l'email nel log invece di inviarla.
    /// In produzione sostituire con SmtpEmailService o un provider (SendGrid, Mailgun, ecc.).
    /// </summary>
    public class LoggingEmailService : IEmailService
    {
        private readonly ILogger<LoggingEmailService> _logger;

        public LoggingEmailService(ILogger<LoggingEmailService> logger)
        {
            _logger = logger;
        }

        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        {
            _logger.LogInformation(
                "[DEV EMAIL] To: {To} | Subject: {Subject}\n{Body}",
                to, subject, htmlBody);

            return Task.CompletedTask;
        }
    }
}
