using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class MockEmailService(ILogger<MockEmailService> logger) : IEmailService
{
    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        logger.LogInformation(
            "[MOCK EMAIL] To: {To} | Subject: {Subject} | Attachments: {Count}",
            message.To, message.Subject, message.Attachments?.Count ?? 0);
        return Task.CompletedTask;
    }

    public Task<bool> TestConnectionAsync(CancellationToken ct)
    {
        logger.LogInformation("[MOCK EMAIL] Connection test — always succeeds");
        return Task.FromResult(true);
    }
}
