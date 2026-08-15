using MediatR;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.Admin;

public record DumpDatabaseQuery : IRequest<DumpDatabaseResult>;

/// <summary>The dump zip, spooled to a delete-on-close temp file so the controller can stream a
/// response of known length without holding the archive in memory.</summary>
public record DumpDatabaseResult(Stream Stream, string FileName);

public class DumpDatabaseHandler(IDatabaseTransferService transfer)
    : IRequestHandler<DumpDatabaseQuery, DumpDatabaseResult>
{
    public async Task<DumpDatabaseResult> Handle(DumpDatabaseQuery request, CancellationToken ct)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"forge-dump-{Guid.NewGuid():N}.zip");
        var temp = new FileStream(
            tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
            bufferSize: 64 * 1024, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            await transfer.WriteDumpZipAsync(temp, ct);
            temp.Position = 0;
            return new DumpDatabaseResult(temp, $"forge-dump-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip");
        }
        catch
        {
            await temp.DisposeAsync(); // DeleteOnClose reclaims the temp file
            throw;
        }
    }
}
