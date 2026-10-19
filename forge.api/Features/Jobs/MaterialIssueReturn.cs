using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

/// <summary>
/// The note a material return writes on its row, which links the return to the issue it reverses. An issue can be
/// returned once.
/// </summary>
public static class MaterialIssueReturn
{
    public const string NotePrefix = "Return of issue #";

    public static string NoteFor(int issueId) => NotePrefix + issueId;

    public static Task<bool> AnyReturnedAsync(AppDbContext db, int jobId, IReadOnlyCollection<int> issueIds, CancellationToken ct)
    {
        var notes = issueIds.Select(NoteFor).ToList();
        return db.MaterialIssues.AnyAsync(
            m => m.JobId == jobId && m.IssueType == MaterialIssueType.Return && m.Notes != null && notes.Contains(m.Notes),
            ct);
    }
}
