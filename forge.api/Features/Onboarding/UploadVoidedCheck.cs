using MediatR;

using Microsoft.AspNetCore.Http;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Onboarding;

/// <summary>
/// Stores a voided check image uploaded during the direct deposit step.
/// Returns a FileAttachmentId included in the OnboardingSubmitRequestModel.
/// </summary>
public record UploadVoidedCheckCommand(
    int UserId,
    IFormFile File) : IRequest<UploadI9DocumentResultModel>;

public class UploadVoidedCheckHandler(AppDbContext db, IStorageService storage)
    : IRequestHandler<UploadVoidedCheckCommand, UploadI9DocumentResultModel>
{
    private const string Bucket = "forge-employee-docs";

    public async Task<UploadI9DocumentResultModel> Handle(
        UploadVoidedCheckCommand request, CancellationToken ct)
    {
        var ext = Path.GetExtension(request.File.FileName);
        var objectKey = $"voided-checks/{request.UserId}/{Guid.NewGuid()}{ext}";

        await using var stream = request.File.OpenReadStream();
        await storage.UploadAsync(Bucket, objectKey, stream, request.File.ContentType, ct);

        var attachment = new FileAttachment
        {
            FileName = request.File.FileName,
            ContentType = request.File.ContentType,
            Size = request.File.Length,
            BucketName = Bucket,
            ObjectKey = objectKey,
            EntityType = "direct-deposit",
            EntityId = request.UserId,
            UploadedById = request.UserId,
            DocumentType = "voided-check",
            Sensitivity = "confidential",
        };

        db.FileAttachments.Add(attachment);
        await db.SaveChangesAsync(ct);

        return new UploadI9DocumentResultModel(attachment.Id, attachment.FileName);
    }
}
