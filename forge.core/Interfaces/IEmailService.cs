using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IEmailService
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(CancellationToken ct);
}
