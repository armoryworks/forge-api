using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Files;

public record UploadFileChunkCommand(
    string EntityType,
    int EntityId,
    string UploadId,
    string FileName,
    string ContentType,
    int ChunkIndex,
    int TotalChunks,
    IFormFile Chunk) : IRequest<ChunkedUploadResponseModel>;

public class UploadFileChunkCommandValidator : AbstractValidator<UploadFileChunkCommand>
{
    private static readonly HashSet<string> ValidEntityTypes =
    [
        "jobs", "expenses", "assets", "parts", "leads", "employees",
        "identity-docs", "employee-docs", "compliance-templates",
        "pay-stubs", "tax-documents",
    ];

    public UploadFileChunkCommandValidator()
    {
        RuleFor(x => x.EntityType)
            .NotEmpty().WithMessage("Entity type is required.")
            .Must(t => ValidEntityTypes.Contains(t)).WithMessage("Invalid entity type.");

        RuleFor(x => x.EntityId)
            .GreaterThan(0).WithMessage("Entity ID is required.");

        RuleFor(x => x.UploadId)
            .NotEmpty().WithMessage("Upload ID is required.")
            .Matches(@"^[a-fA-F0-9\-]{32,36}$").WithMessage("Upload ID must be a valid GUID.");

        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage("File name is required.")
            .MaximumLength(255).WithMessage("File name must not exceed 255 characters.");

        RuleFor(x => x.ChunkIndex)
            .GreaterThanOrEqualTo(0).WithMessage("Chunk index must be 0 or greater.");

        RuleFor(x => x.TotalChunks)
            .GreaterThan(0).WithMessage("Total chunks must be at least 1.");

        RuleFor(x => x.ChunkIndex)
            .LessThan(x => x.TotalChunks).WithMessage("Chunk index must be less than total chunks.");

        RuleFor(x => x.Chunk)
            .NotNull().WithMessage("Chunk data is required.");
    }
}

public class UploadFileChunkHandler(
    IStorageService storage,
    IFileRepository fileRepo,
    IHttpContextAccessor httpContext,
    IOptions<MinioOptions> minioOptions,
    ILogger<UploadFileChunkHandler> logger) : IRequestHandler<UploadFileChunkCommand, ChunkedUploadResponseModel>
{
    private const string TempDirEnvKey = "CHUNK_UPLOAD_TEMP_PATH";
    private const string DefaultTempDir = "/tmp/forge-uploads";

    public async Task<ChunkedUploadResponseModel> Handle(
        UploadFileChunkCommand request,
        CancellationToken cancellationToken)
    {
        var tempDir = GetTempDirectory(request.UploadId);
        Directory.CreateDirectory(tempDir);

        await WriteChunkAsync(tempDir, request, cancellationToken);

        var isLastChunk = request.ChunkIndex == request.TotalChunks - 1;
        if (!isLastChunk)
        {
            return new ChunkedUploadResponseModel(
                request.UploadId,
                request.ChunkIndex,
                IsComplete: false,
                FileAttachment: null);
        }

        var attachment = await AssembleAndUploadAsync(request, tempDir, cancellationToken);
        CleanUpTempDirectory(tempDir, request.UploadId);

        return new ChunkedUploadResponseModel(
            request.UploadId,
            request.ChunkIndex,
            IsComplete: true,
            FileAttachment: attachment);
    }

    private async Task WriteChunkAsync(
        string tempDir,
        UploadFileChunkCommand request,
        CancellationToken cancellationToken)
    {
        var chunkPath = GetChunkPath(tempDir, request.ChunkIndex);
        await using var chunkStream = request.Chunk.OpenReadStream();
        await using var fileStream = new FileStream(
            chunkPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        await chunkStream.CopyToAsync(fileStream, cancellationToken);
    }

    private async Task<FileAttachmentResponseModel> AssembleAndUploadAsync(
        UploadFileChunkCommand request,
        string tempDir,
        CancellationToken cancellationToken)
    {
        var userId = int.Parse(
            httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var bucketName = ResolveBucket(request.EntityType);
        var objectKey = $"{request.EntityType}/{request.EntityId}/{Guid.NewGuid():N}-{request.FileName}";

        using var assembledStream = new MemoryStream();
        for (var i = 0; i < request.TotalChunks; i++)
        {
            var chunkPath = GetChunkPath(tempDir, i);
            if (!File.Exists(chunkPath))
            {
                throw new InvalidOperationException(
                    $"Chunk {i} of {request.TotalChunks} not found for upload '{request.UploadId}'. " +
                    "All chunks must be uploaded before the final chunk is sent.");
            }

            await using var chunkStream = new FileStream(
                chunkPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true);
            await chunkStream.CopyToAsync(assembledStream, cancellationToken);
        }

        assembledStream.Position = 0;
        var totalSize = assembledStream.Length;

        await storage.UploadAsync(bucketName, objectKey, assembledStream, request.ContentType, cancellationToken);

        var attachment = new FileAttachment
        {
            FileName = request.FileName,
            ContentType = request.ContentType,
            Size = totalSize,
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

    private void CleanUpTempDirectory(string tempDir, string uploadId)
    {
        try
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to clean up temp directory for upload {UploadId}", uploadId);
        }
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

    private static string GetTempDirectory(string uploadId) =>
        Path.Combine(
            Environment.GetEnvironmentVariable(TempDirEnvKey) ?? DefaultTempDir,
            uploadId);

    private static string GetChunkPath(string tempDir, int chunkIndex) =>
        Path.Combine(tempDir, $"{chunkIndex:D6}.tmp");
}
