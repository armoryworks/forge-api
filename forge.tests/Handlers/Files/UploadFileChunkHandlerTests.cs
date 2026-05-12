using System.Security.Claims;
using System.Text;

using Bogus;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

using Forge.Api.Features.Files;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Files;

public class UploadFileChunkHandlerTests : IDisposable
{
    private readonly Mock<IStorageService> _storageService = new();
    private readonly Mock<IFileRepository> _fileRepo = new();
    private readonly Mock<IHttpContextAccessor> _httpContextAccessor = new();
    private readonly UploadFileChunkHandler _handler;

    private readonly Faker _faker = new();
    private readonly int _userId;
    private readonly string _tempBasePath;

    public UploadFileChunkHandlerTests()
    {
        _userId = _faker.Random.Int(1, 100);

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, _userId.ToString()) };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        _httpContextAccessor.Setup(a => a.HttpContext).Returns(httpContext);

        var minioOptions = Options.Create(new MinioOptions
        {
            JobFilesBucket = "forge-job-files",
            ReceiptsBucket = "forge-receipts",
            EmployeeDocsBucket = "forge-employee-docs",
        });

        // Use a per-test temp directory so tests can run in parallel without collisions
        _tempBasePath = Path.Combine(Path.GetTempPath(), $"qb-test-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("CHUNK_UPLOAD_TEMP_PATH", _tempBasePath);

        _handler = new UploadFileChunkHandler(
            _storageService.Object,
            _fileRepo.Object,
            _httpContextAccessor.Object,
            minioOptions,
            NullLogger<UploadFileChunkHandler>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempBasePath))
            Directory.Delete(_tempBasePath, recursive: true);

        Environment.SetEnvironmentVariable("CHUNK_UPLOAD_TEMP_PATH", null);
    }

    [Fact]
    public async Task Handle_NonFinalChunk_ReturnIsCompleteFalse()
    {
        // Arrange
        var uploadId = Guid.NewGuid().ToString();
        var command = BuildChunkCommand(uploadId, chunkIndex: 0, totalChunks: 3, content: "chunk-0");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsComplete.Should().BeFalse();
        result.FileAttachment.Should().BeNull();
        result.UploadId.Should().Be(uploadId);
        result.ChunkIndex.Should().Be(0);

        // Storage should NOT have been called yet
        _storageService.Verify(s => s.UploadAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_FinalChunkOfSingleChunkUpload_AssemblesAndUploads()
    {
        // Arrange
        var uploadId = Guid.NewGuid().ToString();
        var fileName = "document.pdf";
        var contentType = "application/pdf";
        var content = "PDF content here";

        // AddAsync callback simulates EF setting the Id (InMemory would do this automatically)
        _fileRepo.Setup(r => r.AddAsync(It.IsAny<FileAttachment>(), It.IsAny<CancellationToken>()))
            .Callback<FileAttachment, CancellationToken>((f, _) => f.Id = 1);

        var expectedAttachment = BuildFileAttachmentResponse(1, fileName, contentType);
        _fileRepo.Setup(r => r.GetByEntityAsync("jobs", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([expectedAttachment]);

        var command = BuildChunkCommand(uploadId, chunkIndex: 0, totalChunks: 1,
            content: content, fileName: fileName, contentType: contentType,
            entityType: "jobs", entityId: 1);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsComplete.Should().BeTrue();
        result.FileAttachment.Should().NotBeNull();

        _storageService.Verify(s => s.UploadAsync(
            "forge-job-files",
            It.Is<string>(k => k.StartsWith("jobs/1/")),
            It.IsAny<Stream>(),
            contentType,
            It.IsAny<CancellationToken>()), Times.Once);

        _fileRepo.Verify(r => r.AddAsync(It.Is<FileAttachment>(f =>
            f.FileName == fileName &&
            f.ContentType == contentType &&
            f.EntityType == "jobs" &&
            f.EntityId == 1 &&
            f.UploadedById == _userId
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MultiChunkUpload_AssemblesChunksInOrder()
    {
        // Arrange
        var uploadId = Guid.NewGuid().ToString();
        var chunk0 = "Hello, ";
        var chunk1 = "World";
        var expectedContent = chunk0 + chunk1;
        var entityType = "jobs";
        var entityId = 42;

        _fileRepo.Setup(r => r.AddAsync(It.IsAny<FileAttachment>(), It.IsAny<CancellationToken>()))
            .Callback<FileAttachment, CancellationToken>((f, _) => f.Id = 1);

        var expectedAttachment = BuildFileAttachmentResponse(1, "test.txt", "text/plain");
        _fileRepo.Setup(r => r.GetByEntityAsync(entityType, entityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([expectedAttachment]);

        // Upload chunk 0
        var cmd0 = BuildChunkCommand(uploadId, chunkIndex: 0, totalChunks: 2,
            content: chunk0, entityType: entityType, entityId: entityId);
        await _handler.Handle(cmd0, CancellationToken.None);

        // Upload chunk 1 (final)
        var cmd1 = BuildChunkCommand(uploadId, chunkIndex: 1, totalChunks: 2,
            content: chunk1, entityType: entityType, entityId: entityId);

        string? capturedContent = null;
        _storageService.Setup(s => s.UploadAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, Stream, string, CancellationToken>((_, _, stream, _, _) =>
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);
                capturedContent = reader.ReadToEnd();
            })
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(cmd1, CancellationToken.None);

        // Assert
        result.IsComplete.Should().BeTrue();
        capturedContent.Should().Be(expectedContent);
    }

    [Fact]
    public async Task Handle_FinalChunk_CleansUpTempDirectory()
    {
        // Arrange
        var uploadId = Guid.NewGuid().ToString();
        var tempDir = Path.Combine(_tempBasePath, uploadId);

        _fileRepo.Setup(r => r.AddAsync(It.IsAny<FileAttachment>(), It.IsAny<CancellationToken>()))
            .Callback<FileAttachment, CancellationToken>((f, _) => f.Id = 1);

        var expectedAttachment = BuildFileAttachmentResponse(1, "file.txt", "text/plain");
        _fileRepo.Setup(r => r.GetByEntityAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([expectedAttachment]);

        var command = BuildChunkCommand(uploadId, chunkIndex: 0, totalChunks: 1, content: "data");

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert — temp directory should be deleted after assembly
        Directory.Exists(tempDir).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ExpenseEntityType_UsesReceiptsBucket()
    {
        // Arrange
        var uploadId = Guid.NewGuid().ToString();
        var entityType = "expenses";
        var entityId = 7;

        _fileRepo.Setup(r => r.AddAsync(It.IsAny<FileAttachment>(), It.IsAny<CancellationToken>()))
            .Callback<FileAttachment, CancellationToken>((f, _) => f.Id = 1);

        var expectedAttachment = BuildFileAttachmentResponse(1, "receipt.jpg", "image/jpeg");
        _fileRepo.Setup(r => r.GetByEntityAsync(entityType, entityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([expectedAttachment]);

        var command = BuildChunkCommand(uploadId, chunkIndex: 0, totalChunks: 1,
            content: "jpg data", entityType: entityType, entityId: entityId,
            contentType: "image/jpeg");

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _storageService.Verify(s => s.UploadAsync(
            "forge-receipts",
            It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_EmployeeEntityType_UsesEmployeeDocsBucket()
    {
        // Arrange
        var uploadId = Guid.NewGuid().ToString();
        var entityType = "employees";
        var entityId = 3;

        _fileRepo.Setup(r => r.AddAsync(It.IsAny<FileAttachment>(), It.IsAny<CancellationToken>()))
            .Callback<FileAttachment, CancellationToken>((f, _) => f.Id = 1);

        var expectedAttachment = BuildFileAttachmentResponse(1, "contract.pdf", "application/pdf");
        _fileRepo.Setup(r => r.GetByEntityAsync(entityType, entityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([expectedAttachment]);

        var command = BuildChunkCommand(uploadId, chunkIndex: 0, totalChunks: 1,
            content: "pdf data", entityType: entityType, entityId: entityId);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _storageService.Verify(s => s.UploadAsync(
            "forge-employee-docs",
            It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_MissingChunkOnFinalAssembly_ThrowsInvalidOperationException()
    {
        // Arrange
        var uploadId = Guid.NewGuid().ToString();

        // Only upload chunk 0, skip chunk 1, then send chunk 2 as "final"
        var cmd0 = BuildChunkCommand(uploadId, chunkIndex: 0, totalChunks: 3, content: "chunk-0");
        await _handler.Handle(cmd0, CancellationToken.None);

        // Skip chunk 1 and go straight to chunk 2 (the final one)
        var cmd2 = BuildChunkCommand(uploadId, chunkIndex: 2, totalChunks: 3, content: "chunk-2");

        // Act
        var act = () => _handler.Handle(cmd2, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Chunk 1 of 3*not found*");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static UploadFileChunkCommand BuildChunkCommand(
        string uploadId,
        int chunkIndex,
        int totalChunks,
        string content,
        string fileName = "test.txt",
        string contentType = "text/plain",
        string entityType = "jobs",
        int entityId = 1)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        var formFile = new FormFile(stream, 0, bytes.Length, "chunk", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };

        return new UploadFileChunkCommand(
            entityType, entityId, uploadId, fileName, contentType,
            chunkIndex, totalChunks, formFile);
    }

    private static FileAttachmentResponseModel BuildFileAttachmentResponse(
        int id,
        string fileName,
        string contentType) =>
        new(id, fileName, contentType, 100, "http://minio/test", "jobs", 1, 1, "Test User",
            DateTime.UtcNow, null, null);
}
