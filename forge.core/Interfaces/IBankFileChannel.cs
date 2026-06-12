namespace Forge.Core.Interfaces;

/// <summary>
/// ⚡ BANKING BOUNDARY — the bank file-exchange channel (BANK-002 Phase B). One generic
/// SFTP implementation covers nearly every bank/CU; the per-bank variance is pure
/// configuration (host, credentials, directories — the banking.sftp.* settings).
/// The manual channel is the null implementation: generate → download → portal upload by
/// hand, with release as the attestation.
/// </summary>
public interface IBankFileChannel
{
    /// <summary>Uploads one generated NACHA file to the bank's drop directory.</summary>
    Task UploadAsync(string fileName, string contents, CancellationToken ct);

    /// <summary>Unprocessed return/NOC file names waiting in the bank's returns directory.</summary>
    Task<IReadOnlyList<string>> ListReturnFilesAsync(CancellationToken ct);

    /// <summary>Downloads one return file's contents.</summary>
    Task<string> DownloadReturnFileAsync(string fileName, CancellationToken ct);

    /// <summary>Marks a return file processed (renamed with a suffix so it is never re-listed).</summary>
    Task MarkReturnProcessedAsync(string fileName, CancellationToken ct);
}
