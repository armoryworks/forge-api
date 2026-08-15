using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>
/// Admin-facing database dump / clean-rebuild import (the in-app counterpart of forge-db's
/// <c>dump</c>/<c>import</c> verbs — same archive layout, zipped, so CLI and UI dumps are
/// interchangeable). Dump is read-only; import truncates the selected tables and reloads them
/// from the archive, which is why it sits behind the Admin role and an explicit confirmation.
/// </summary>
public interface IDatabaseTransferService
{
    /// <summary>Application tables with row estimates/sizes — the UI's preview surface.</summary>
    Task<DatabaseTransferSummaryModel> GetSummaryAsync(CancellationToken ct = default);

    /// <summary>Stream a zip of the full data dump (tables/*.copy + manifest.json) to <paramref name="output"/>.</summary>
    Task WriteDumpZipAsync(Stream output, CancellationToken ct = default);

    /// <summary>Load a dump zip into this database (truncate + reload the selected tables).</summary>
    Task<DatabaseImportReportModel> ImportZipAsync(
        Stream zipFile, DatabaseImportOptionsModel options, CancellationToken ct = default);
}
