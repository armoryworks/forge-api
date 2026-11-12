using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

public record DownloadSubmissionPdfQuery(
    int SubmissionId,
    int RequestingUserId,
    bool IsPrivileged) : IRequest<SubmissionPdfResult>;

public record SubmissionPdfResult(byte[] Bytes, string FileName);

public class DownloadSubmissionPdfHandler(AppDbContext db, IStorageService storageService, IHtmlPdfRenderer renderer, IClock clock)
    : IRequestHandler<DownloadSubmissionPdfQuery, SubmissionPdfResult>
{
    public async Task<SubmissionPdfResult> Handle(
        DownloadSubmissionPdfQuery request, CancellationToken ct)
    {
        var submission = await db.ComplianceFormSubmissions
            .Include(s => s.Template)
            .Include(s => s.FormDefinitionVersion)
            .Include(s => s.SignedPdfFile)
            .FirstOrDefaultAsync(
                s => s.Id == request.SubmissionId
                     && (request.IsPrivileged || s.UserId == request.RequestingUserId), ct)
            ?? throw new KeyNotFoundException($"Submission {request.SubmissionId} not found.");

        var safeName = new string(
            (submission.Template?.Name ?? "form")
            .Select(c => char.IsLetterOrDigit(c) || c == '-' || c == ' ' ? c : '_')
            .ToArray()).Trim();
        var fileName = $"{safeName}.pdf";

        if (submission.SignedPdfFile is not null)
        {
            using var stream = await storageService.DownloadAsync(
                submission.SignedPdfFile.BucketName,
                submission.SignedPdfFile.ObjectKey, ct);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            return new SubmissionPdfResult(ms.ToArray(), fileName);
        }

        var bytes = await renderer.RenderAsync(ComplianceFormSubmissionPdfDocument.Build(submission, clock.UtcNow), ct);
        return new SubmissionPdfResult(bytes, fileName);
    }
}
