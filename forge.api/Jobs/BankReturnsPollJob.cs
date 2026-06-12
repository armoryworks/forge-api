using Microsoft.Extensions.Logging;

using Forge.Api.Features.Banking;
using Forge.Core.Interfaces;
using Forge.Core.Settings;

namespace Forge.Api.Jobs;

/// <summary>
/// ⚡ BANKING BOUNDARY — BANK-002 Phase C poll: when the submission channel is SFTP, pulls the
/// bank's return/NOC files on a schedule, applies them (idempotently), and renames each file
/// ".processed" so it is never re-listed. A no-op on the manual channel — there, returns are
/// imported by hand via POST /banking/returns/import.
/// </summary>
public class BankReturnsPollJob(
    ISettingsService settings,
    IBankFileChannel channel,
    IBankReturnsService returns,
    ILogger<BankReturnsPollJob> logger)
{
    public async Task PollAsync(CancellationToken ct)
    {
        var channelSetting = (await settings.GetStringAsync(BankingSettings.ChannelKey, ct))?.Trim().ToLowerInvariant();
        if (channelSetting != "sftp")
            return; // manual channel — nothing to poll

        var files = await channel.ListReturnFilesAsync(ct);
        foreach (var fileName in files)
        {
            try
            {
                var contents = await channel.DownloadReturnFileAsync(fileName, ct);
                var result = await returns.ApplyAsync(contents, actorUserId: null, ct);
                await channel.MarkReturnProcessedAsync(fileName, ct);
                logger.LogInformation(
                    "Bank returns file {FileName}: {Returned} return(s), {Prenotes} prenote rejection(s), {Nocs} NOC(s).",
                    fileName, result.PaymentsReturned, result.PrenotesRejected, result.Nocs);
            }
            catch (Exception ex)
            {
                // Leave the file unprocessed — the next poll retries it; apply is idempotent.
                logger.LogError(ex, "Bank returns file {FileName} failed to apply; will retry next poll.", fileName);
            }
        }
    }
}
