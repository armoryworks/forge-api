using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Chat;

public record UploadChatFileCommand(int ChannelId, IFormFile File) : IRequest<FileAttachmentResponseModel>;

public class UploadChatFileCommandValidator : AbstractValidator<UploadChatFileCommand>
{
    private static readonly HashSet<string> AllowedExtensions =
    [
        ".pdf", ".jpg", ".jpeg", ".png", ".gif",
        ".doc", ".docx", ".xls", ".xlsx", ".csv", ".txt",
    ];

    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10MB

    public UploadChatFileCommandValidator()
    {
        RuleFor(x => x.ChannelId)
            .GreaterThan(0).WithMessage("Channel ID is required.");

        RuleFor(x => x.File)
            .NotNull().WithMessage("File is required.")
            .Must(f => f == null || f.Length <= MaxFileSizeBytes)
                .WithMessage("File size must not exceed 10MB.")
            .Must(f =>
            {
                if (f == null) return true;
                var ext = Path.GetExtension(f.FileName)?.ToLowerInvariant();
                return ext != null && AllowedExtensions.Contains(ext);
            })
                .WithMessage($"Allowed file types: {string.Join(", ", AllowedExtensions)}");
    }
}

public class UploadChatFileHandler(
    IStorageService storage,
    IFileRepository fileRepo,
    IHttpContextAccessor httpContext,
    IOptions<MinioOptions> minioOptions) : IRequestHandler<UploadChatFileCommand, FileAttachmentResponseModel>
{
    public async Task<FileAttachmentResponseModel> Handle(UploadChatFileCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var file = request.File;

        var bucketName = minioOptions.Value.JobFilesBucket;
        var objectKey = $"chat/{request.ChannelId}/{Guid.NewGuid():N}-{file.FileName}";

        await using var stream = file.OpenReadStream();
        await storage.UploadAsync(bucketName, objectKey, stream, file.ContentType, cancellationToken);

        var attachment = new FileAttachment
        {
            FileName = file.FileName,
            ContentType = file.ContentType,
            Size = file.Length,
            BucketName = bucketName,
            ObjectKey = objectKey,
            EntityType = "chat",
            EntityId = request.ChannelId,
            UploadedById = userId,
        };

        await fileRepo.AddAsync(attachment, cancellationToken);

        var files = await fileRepo.GetByEntityAsync("chat", request.ChannelId, cancellationToken);
        return files.First(f => f.Id == attachment.Id);
    }
}
