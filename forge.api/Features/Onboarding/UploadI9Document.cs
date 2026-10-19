using FluentValidation;
using MediatR;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Files;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Onboarding;

/// <summary>
/// Pre-uploads an I-9 identity document file before the onboarding submission is finalized.
/// Returns a FileAttachment ID that is referenced in the OnboardingSubmitRequestModel.
/// The IdentityDocument entity is created and linked during SubmitOnboardingHandler.
/// </summary>
public record UploadI9DocumentCommand(
    int UserId,
    IFormFile File,
    string DocumentList) : IRequest<UploadI9DocumentResultModel>;

public record UploadI9DocumentResultModel(int FileAttachmentId, string FileName);

public class UploadI9DocumentValidator : AbstractValidator<UploadI9DocumentCommand>
{
    public UploadI9DocumentValidator()
    {
        RuleFor(x => x.File)
            .NotNull().WithMessage("File is required.");

        RuleFor(x => x.File.FileName)
            .Must(name => FileEntityTypes.IsAllowedFileName("employee-docs", name))
            .When(x => x.File is not null)
            .WithMessage("This file type cannot be uploaded here.");
    }
}

public class UploadI9DocumentHandler(AppDbContext db, IStorageService storage)
    : IRequestHandler<UploadI9DocumentCommand, UploadI9DocumentResultModel>
{
    private const string Bucket = "forge-employee-docs";

    public async Task<UploadI9DocumentResultModel> Handle(
        UploadI9DocumentCommand request, CancellationToken ct)
    {
        var ext = Path.GetExtension(FileEntityTypes.ObjectKeyName(request.File.FileName));
        var objectKey = $"i9-docs/{request.UserId}/{Guid.NewGuid()}{ext}";

        await using var stream = request.File.OpenReadStream();
        await storage.UploadAsync(Bucket, objectKey, stream, FileEntityTypes.NormalizeContentType(request.File.ContentType), ct);

        var attachment = new FileAttachment
        {
            FileName = FileEntityTypes.DisplayName(request.File.FileName),
            ContentType = FileEntityTypes.NormalizeContentType(request.File.ContentType),
            Size = request.File.Length,
            BucketName = Bucket,
            ObjectKey = objectKey,
            EntityType = "identity-documents",
            EntityId = 0, // Linked to IdentityDocument.Id after submission
            UploadedById = request.UserId,
            DocumentType = request.DocumentList,
            Sensitivity = "confidential",
        };

        db.FileAttachments.Add(attachment);
        await db.SaveChangesAsync(ct);

        return new UploadI9DocumentResultModel(attachment.Id, attachment.FileName);
    }
}
