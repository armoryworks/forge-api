using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Forge.Api.Authorization;
using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Files;

public record UploadFileCommand(string EntityType, int EntityId, IFormFile File, string? DocumentType = null)
    : IRequest<FileAttachmentResponseModel>;

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

        RuleFor(x => x.File.FileName)
            .Must((x, name) => FileEntityTypes.IsAllowedFileName(x.EntityType, name))
            .When(x => x.File is not null)
            .WithMessage("This file type cannot be uploaded here.");
    }
}

public class UploadFileHandler(
    IStorageService storage,
    IFileRepository fileRepo,
    IHttpContextAccessor httpContext,
    IOptions<MinioOptions> minioOptions,
    IEntityAccessResolver access) : IRequestHandler<UploadFileCommand, FileAttachmentResponseModel>
{
    public async Task<FileAttachmentResponseModel> Handle(UploadFileCommand request, CancellationToken cancellationToken)
    {
        var user = httpContext.HttpContext!.User;
        if (!await access.CanAccessAsync(request.EntityType, request.EntityId, user, forWrite: true, cancellationToken))
            throw new ForbiddenException("You do not have permission to attach files to this record.");

        var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var file = request.File;

        var bucketName = FileEntityTypes.ResolveBucket(request.EntityType, minioOptions.Value);
        var objectKey = $"{request.EntityType}/{request.EntityId}/{Guid.NewGuid():N}-{FileEntityTypes.ObjectKeyName(file.FileName)}";
        var contentType = FileEntityTypes.NormalizeContentType(file.ContentType);

        await using var stream = file.OpenReadStream();
        await storage.UploadAsync(bucketName, objectKey, stream, contentType, cancellationToken);

        var attachment = new FileAttachment
        {
            FileName = FileEntityTypes.DisplayName(file.FileName),
            ContentType = contentType,
            Size = file.Length,
            BucketName = bucketName,
            ObjectKey = objectKey,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            UploadedById = userId,
            DocumentType = request.DocumentType,
        };

        await fileRepo.AddAsync(attachment, cancellationToken);

        var files = await fileRepo.GetByEntityAsync(request.EntityType, request.EntityId, cancellationToken);
        return files.First(f => f.Id == attachment.Id);
    }
}
