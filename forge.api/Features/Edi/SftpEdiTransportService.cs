using Renci.SshNet;

using Forge.Api.Services;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Serilog;

namespace Forge.Api.Features.Edi;

/// <summary>
/// ⚡ EDI BOUNDARY — generic SFTP implementation of <see cref="IEdiTransportService"/> (SSH.NET,
/// MIT). One implementation covers nearly every trading partner; the per-partner variance is the
/// typed config on the partner row (host/credentials/directories — entered as labeled admin
/// fields, password encrypted at rest). Polled inbound files are renamed ".processed" once read
/// so they're never re-listed; outbound files are named forge-{utc-stamp}.edi.
/// </summary>
public sealed class SftpEdiTransportService(
    IEdiCredentialProtector protector,
    IClock clock) : IEdiTransportService
{
    private const string ProcessedSuffix = ".processed";

    public EdiTransportMethod Method => EdiTransportMethod.Sftp;

    public Task SendAsync(string payload, string connectionConfig, CancellationToken ct)
    {
        var (client, config) = Connect(connectionConfig);
        using (client)
        {
            var fileName = $"forge-{clock.UtcNow:yyyyMMddHHmmssfff}.edi";
            using var stream = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(payload));
            client.UploadFile(stream, $"{config.OutboundDir.TrimEnd('/')}/{fileName}");
            Log.Information("EDI SFTP: sent {FileName} to {Host}.", fileName, config.Host);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> PollAsync(string connectionConfig, CancellationToken ct)
    {
        var (client, config) = Connect(connectionConfig);
        using (client)
        {
            var dir = config.InboundDir.TrimEnd('/');
            var payloads = new List<string>();
            var files = client.ListDirectory(dir)
                .Where(f => f.IsRegularFile && !f.Name.EndsWith(ProcessedSuffix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Name, StringComparer.Ordinal)
                .ToList();

            foreach (var file in files)
            {
                using var stream = new MemoryStream();
                client.DownloadFile($"{dir}/{file.Name}", stream);
                payloads.Add(System.Text.Encoding.ASCII.GetString(stream.ToArray()));
                // Consume-once: the payload is persisted as an EdiTransaction by the caller;
                // retries thereafter happen at the transaction level, not by re-reading the file.
                client.RenameFile($"{dir}/{file.Name}", $"{dir}/{file.Name}{ProcessedSuffix}");
            }

            Log.Information("EDI SFTP: polled {Count} file(s) from {Host}.", payloads.Count, config.Host);
            return Task.FromResult<IReadOnlyList<string>>(payloads);
        }
    }

    public Task<bool> TestConnectionAsync(string connectionConfig, CancellationToken ct)
    {
        try
        {
            var (client, _) = Connect(connectionConfig);
            using (client)
            {
                return Task.FromResult(client.IsConnected);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "EDI SFTP connection test failed.");
            return Task.FromResult(false);
        }
    }

    private (SftpClient Client, EdiSftpTransportConfig Config) Connect(string connectionConfig)
    {
        var config = EdiSftpTransportConfig.FromJson(connectionConfig)
            ?? throw new InvalidOperationException(
                "The trading partner's SFTP transport is not configured — fill in the transport fields on the partner.");

        var password = protector.Unprotect(config.PasswordEncrypted)
            ?? throw new InvalidOperationException(
                "The trading partner's SFTP password is missing — re-enter it on the partner.");

        var client = new SftpClient(config.Host, config.Port > 0 ? config.Port : 22, config.Username, password);
        client.Connect();
        return (client, config);
    }
}
