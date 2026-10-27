using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Forge.Api.Capabilities;
using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Chat;

[RequiresCapability("CAP-CROSS-ATTACHMENTS")]
public record UploadDirectMessageFileCommand(int RecipientId, IFormFile File) : IRequest<FileAttachmentResponseModel>;

public class UploadDirectMessageFileCommandValidator : AbstractValidator<UploadDirectMessageFileCommand>
{
    public UploadDirectMessageFileCommandValidator()
    {
        RuleFor(x => x.RecipientId)
            .GreaterThan(0).WithMessage("Recipient is required.");

        RuleFor(x => x.File)
            .NotNull().WithMessage("File is required.")
            .Must(f => f == null || f.Length <= ChatFileRules.MaxFileSizeBytes)
                .WithMessage("File size must not exceed 10MB.").WithErrorCode("file-too-large")
            .Must(ChatFileRules.HasAllowedExtension)
                .WithMessage($"Allowed file types: {string.Join(", ", ChatFileRules.AllowedExtensions)}");
    }
}

public class UploadDirectMessageFileHandler(
    AppDbContext db,
    IStorageService storage,
    IFileRepository fileRepo,
    IHttpContextAccessor httpContext,
    IOptions<MinioOptions> minioOptions) : IRequestHandler<UploadDirectMessageFileCommand, FileAttachmentResponseModel>
{
    public async Task<FileAttachmentResponseModel> Handle(UploadDirectMessageFileCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var file = request.File;

        if (request.RecipientId == userId)
            throw new ForbiddenException("You cannot send a file to yourself.");

        if (!await db.Users.AnyAsync(u => u.Id == request.RecipientId && u.IsActive, cancellationToken))
            throw new KeyNotFoundException($"User {request.RecipientId} not found");

        var bucketName = minioOptions.Value.JobFilesBucket;
        var objectKey = $"chat-dm/{Math.Min(userId, request.RecipientId)}-{Math.Max(userId, request.RecipientId)}/{Guid.NewGuid():N}-{file.FileName}";

        await using var stream = file.OpenReadStream();
        await storage.UploadAsync(bucketName, objectKey, stream, file.ContentType, cancellationToken);

        var attachment = new FileAttachment
        {
            FileName = file.FileName,
            ContentType = file.ContentType,
            Size = file.Length,
            BucketName = bucketName,
            ObjectKey = objectKey,
            EntityType = ChatFileRules.DirectMessageEntityType,
            EntityId = request.RecipientId,
            UploadedById = userId,
        };

        await fileRepo.AddAsync(attachment, cancellationToken);

        var files = await fileRepo.GetByEntityAsync(ChatFileRules.DirectMessageEntityType, request.RecipientId, cancellationToken);
        return files.First(f => f.Id == attachment.Id);
    }
}
