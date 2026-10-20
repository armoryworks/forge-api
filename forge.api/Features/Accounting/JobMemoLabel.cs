using System.Globalization;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Terminology;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// The job reference written into GL memos and line descriptions: the install's term for a job plus the
/// job number (e.g. "Work Order J-4"), resolved at posting time so the posted text stays as it was written.
/// </summary>
internal static class JobMemoLabel
{
    public static async Task<string> ResolveAsync(
        AppDbContext db, ITerminologyLabels? terminology, int jobId, CancellationToken ct)
    {
        var terms = terminology is null ? TermLabels.Defaults : await terminology.GetAsync(ct);
        var jobNumber = await db.Jobs.IgnoreQueryFilters().AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => j.JobNumber)
            .FirstOrDefaultAsync(ct);
        return $"{terms.Singular("job")} {(string.IsNullOrWhiteSpace(jobNumber) ? jobId.ToString(CultureInfo.InvariantCulture) : jobNumber)}";
    }
}
