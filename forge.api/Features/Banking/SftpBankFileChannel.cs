using Renci.SshNet;

using Forge.Core.Interfaces;
using Forge.Core.Settings;
using Serilog;

namespace Forge.Api.Features.Banking;

/// <summary>
/// ⚡ BANKING BOUNDARY — generic SFTP implementation of <see cref="IBankFileChannel"/> (SSH.NET,
/// MIT). Everything bank-specific is the banking.sftp.* settings group: host, port, username,
/// password (encrypted at rest), upload + returns directories. Processed return files are
/// renamed with a ".processed" suffix rather than deleted — the bank's drop stays auditable.
/// Connections are per-operation (bank drops are low-frequency; no pooling complexity).
/// </summary>
public sealed class SftpBankFileChannel(ISettingsService settings) : IBankFileChannel
{
    private const string ProcessedSuffix = ".processed";

    public async Task UploadAsync(string fileName, string contents, CancellationToken ct)
    {
        var (client, uploadDir, _) = await ConnectAsync(ct);
        using (client)
        {
            var remotePath = $"{uploadDir.TrimEnd('/')}/{fileName}";
            using var stream = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(contents));
            client.UploadFile(stream, remotePath);
            Log.Information("Bank SFTP: uploaded {FileName} to {Dir}.", fileName, uploadDir);
        }
    }

    public async Task<IReadOnlyList<string>> ListReturnFilesAsync(CancellationToken ct)
    {
        var (client, _, returnsDir) = await ConnectAsync(ct);
        using (client)
        {
            return client.ListDirectory(returnsDir)
                .Where(f => f.IsRegularFile && !f.Name.EndsWith(ProcessedSuffix, StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
        }
    }

    public async Task<string> DownloadReturnFileAsync(string fileName, CancellationToken ct)
    {
        var (client, _, returnsDir) = await ConnectAsync(ct);
        using (client)
        {
            using var stream = new MemoryStream();
            client.DownloadFile($"{returnsDir.TrimEnd('/')}/{fileName}", stream);
            return System.Text.Encoding.ASCII.GetString(stream.ToArray());
        }
    }

    public async Task MarkReturnProcessedAsync(string fileName, CancellationToken ct)
    {
        var (client, _, returnsDir) = await ConnectAsync(ct);
        using (client)
        {
            var dir = returnsDir.TrimEnd('/');
            client.RenameFile($"{dir}/{fileName}", $"{dir}/{fileName}{ProcessedSuffix}");
        }
    }

    private async Task<(SftpClient Client, string UploadDir, string ReturnsDir)> ConnectAsync(CancellationToken ct)
    {
        var host = await Required(BankingSettings.SftpHostKey, ct);
        var port = int.TryParse(await settings.GetStringAsync(BankingSettings.SftpPortKey, ct), out var p) ? p : 22;
        var username = await Required(BankingSettings.SftpUsernameKey, ct);
        var password = await Required(BankingSettings.SftpPasswordKey, ct);
        var uploadDir = await settings.GetStringAsync(BankingSettings.SftpUploadDirKey, ct) ?? "/inbound";
        var returnsDir = await settings.GetStringAsync(BankingSettings.SftpReturnsDirKey, ct) ?? "/outbound";

        var client = new SftpClient(host, port, username, password);
        client.Connect();
        return (client, uploadDir, returnsDir);
    }

    private async Task<string> Required(string key, CancellationToken ct)
        => await settings.GetStringAsync(key, ct) is { Length: > 0 } v
            ? v
            : throw new InvalidOperationException(
                $"Bank SFTP setting '{key}' is not configured — complete the Banking → SFTP settings "
                + "before using the sftp channel.");
}
