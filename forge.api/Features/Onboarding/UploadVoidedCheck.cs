using FluentValidation;
using MediatR;

using Microsoft.AspNetCore.Http;

using Forge.Api.Features.Files;
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

public class UploadVoidedCheckValidator : AbstractValidator<UploadVoidedCheckCommand>
{
    public UploadVoidedCheckValidator()
    {
        RuleFor(x => x.File)
            .NotNull().WithMessage("File is required.");

        RuleFor(x => x.File.FileName)
            .Must(name => FileEntityTypes.IsAllowedFileName("employee-docs", name))
            .When(x => x.File is not null)
            .WithMessage("This file type cannot be uploaded here.");
    }
}

public class UploadVoidedCheckHandler(AppDbContext db, IStorageService storage)
    : IRequestHandler<UploadVoidedCheckCommand, UploadI9DocumentResultModel>
{
    private const string Bucket = "forge-employee-docs";

    public async Task<UploadI9DocumentResultModel> Handle(
        UploadVoidedCheckCommand request, CancellationToken ct)
    {
        var ext = Path.GetExtension(FileEntityTypes.ObjectKeyName(request.File.FileName));
        var objectKey = $"voided-checks/{request.UserId}/{Guid.NewGuid()}{ext}";

        await using var stream = request.File.OpenReadStream();
        await storage.UploadAsync(Bucket, objectKey, stream, FileEntityTypes.NormalizeContentType(request.File.ContentType), ct);

        var attachment = new FileAttachment
        {
            FileName = FileEntityTypes.DisplayName(request.File.FileName),
            ContentType = FileEntityTypes.NormalizeContentType(request.File.ContentType),
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
