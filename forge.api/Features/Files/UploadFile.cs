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
    public UploadFileCommandValidator()
    {
        RuleFor(x => x.EntityType)
            .NotEmpty().WithMessage("Entity type is required.")
            .Must(t => FileEntityTypes.Valid.Contains(t)).WithMessage("Invalid entity type.");

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

        var bucketName = FileEntityTypes.ResolveBucket(request.EntityType, minioOptions.Value);
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
}
