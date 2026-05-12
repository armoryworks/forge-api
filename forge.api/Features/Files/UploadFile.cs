using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Files;

public record UploadFileCommand(string EntityType, int EntityId, IFormFile File) : IRequest<FileAttachmentResponseModel>;

public class UploadFileCommandValidator : AbstractValidator<UploadFileCommand>
{
    private static readonly HashSet<string> ValidEntityTypes =
    [
        "jobs", "expenses", "assets", "parts", "leads", "employees",
        "identity-docs", "employee-docs", "compliance-templates",
        "pay-stubs", "tax-documents",
    ];

    public UploadFileCommandValidator()
    {
        RuleFor(x => x.EntityType)
            .NotEmpty().WithMessage("Entity type is required.")
            .Must(t => ValidEntityTypes.Contains(t)).WithMessage("Invalid entity type.");

        RuleFor(x => x.EntityId)
            .GreaterThan(0).WithMessage("Entity ID is required.");

        RuleFor(x => x.File)
            .NotNull().WithMessage("File is required.");
    }
}

public class UploadFileHandler(
    IStorageService storage,
    IFileRepository fileRepo,
    IHttpContextAccessor httpContext,
    IOptions<MinioOptions> minioOptions) : IRequestHandler<UploadFileCommand, FileAttachmentResponseModel>
{
    public async Task<FileAttachmentResponseModel> Handle(UploadFileCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var file = request.File;

        var bucketName = ResolveBucket(request.EntityType);
        var objectKey = $"{request.EntityType}/{request.EntityId}/{Guid.NewGuid():N}-{file.FileName}";

        await using var stream = file.OpenReadStream();
        await storage.UploadAsync(bucketName, objectKey, stream, file.ContentType, cancellationToken);

        var attachment = new FileAttachment
        {
            FileName = file.FileName,
            ContentType = file.ContentType,
            Size = file.Length,
            BucketName = bucketName,
            ObjectKey = objectKey,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            UploadedById = userId,
        };

        await fileRepo.AddAsync(attachment, cancellationToken);

        var files = await fileRepo.GetByEntityAsync(request.EntityType, request.EntityId, cancellationToken);
        return files.First(f => f.Id == attachment.Id);
    }

    private string ResolveBucket(string entityType)
    {
        var opts = minioOptions.Value;
        return entityType switch
        {
            "expenses" => opts.ReceiptsBucket,
            "employees" or "identity-docs" or "employee-docs"
                or "compliance-templates" or "pay-stubs" or "tax-documents" => opts.EmployeeDocsBucket,
            _ => opts.JobFilesBucket,
        };
    }
}
