using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;

using Forge.Api.Authorization;
using Forge.Api.Features.Files;
using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Files;

/// <summary>
/// The files endpoints used to serve, list and delete any attachment by id to any
/// signed-in user. These pin the entity access check in each handler and the
/// uploader-or-supervisor rule on delete.
/// </summary>
public class FileAccessHandlerTests
{
    private const int CallerId = 5;

    private readonly Mock<IFileRepository> _fileRepo = new();
    private readonly Mock<IStorageService> _storage = new();
    private readonly Mock<IClock> _clock = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private static IHttpContextAccessor As(params string[] roles) => new HttpContextAccessor
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                roles.Select(r => new Claim(ClaimTypes.Role, r))
                    .Prepend(new Claim(ClaimTypes.NameIdentifier, CallerId.ToString())),
                "Test")),
        },
    };

    private EntityAccessResolver Resolver() => new(TestDbContextFactory.Create());

    private FileAttachment Stored(string entityType, int uploadedById = 9)
    {
        var file = new FileAttachment
        {
            Id = 42, EntityType = entityType, EntityId = 3, UploadedById = uploadedById,
            BucketName = "forge-job-files", ObjectKey = "k", ContentType = "application/pdf", FileName = "a.pdf",
        };
        _fileRepo.Setup(r => r.FindAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(file);
        return file;
    }

    [Fact]
    public async Task Download_refuses_an_invoice_file_to_a_production_worker()
    {
        Stored("invoices");
        var handler = new DownloadFileHandler(_fileRepo.Object, _storage.Object, Resolver(), As("ProductionWorker"));

        var act = () => handler.Handle(new DownloadFileQuery(42), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        _storage.Verify(s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Download_serves_a_job_file_to_a_production_worker()
    {
        Stored("jobs");
        _storage.Setup(s => s.DownloadAsync("forge-job-files", "k", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream([1]));
        var handler = new DownloadFileHandler(_fileRepo.Object, _storage.Object, Resolver(), As("ProductionWorker"));

        var result = await handler.Handle(new DownloadFileQuery(42), CancellationToken.None);

        result.FileName.Should().Be("a.pdf");
    }

    [Fact]
    public async Task Listing_refuses_a_record_the_caller_cannot_open()
    {
        var handler = new GetFilesHandler(_fileRepo.Object, Resolver(), As("ProductionWorker"));

        var act = () => handler.Handle(new GetFilesQuery("pay-stubs", 99), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        _fileRepo.Verify(r => r.GetByEntityAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_refuses_someone_elses_file_to_a_non_supervisor()
    {
        Stored("jobs", uploadedById: 9);
        var handler = new DeleteFileHandler(_fileRepo.Object, _db, Resolver(), As("Engineer"), _clock.Object);

        var act = () => handler.Handle(new DeleteFileCommand(42), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        _fileRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_lets_the_uploader_remove_their_own_file()
    {
        var file = Stored("jobs", uploadedById: CallerId);
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        _clock.Setup(c => c.UtcNow).Returns(now);
        var handler = new DeleteFileHandler(_fileRepo.Object, _db, Resolver(), As("Engineer"), _clock.Object);

        await handler.Handle(new DeleteFileCommand(42), CancellationToken.None);

        file.DeletedAt.Should().Be(now);
        file.DeletedBy.Should().Be(CallerId.ToString());
    }

    [Fact]
    public async Task Delete_refuses_a_supervisor_outside_the_records_roles()
    {
        Stored("assets", uploadedById: 9);
        var handler = new DeleteFileHandler(_fileRepo.Object, _db, Resolver(), As("OfficeManager"), _clock.Object);

        var act = () => handler.Handle(new DeleteFileCommand(42), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Theory]
    [InlineData(AcceptanceStatus.Accepted)]
    [InlineData(AcceptanceStatus.Pending)]
    public async Task Delete_refuses_the_uploader_when_a_live_attestation_holds_the_file(AcceptanceStatus status)
    {
        Stored("jobs", uploadedById: CallerId);
        _db.Attestations.Add(new Attestation { SalesOrderId = 3, Status = status, FileAttachmentId = 42 });
        await _db.SaveChangesAsync();
        var handler = new DeleteFileHandler(_fileRepo.Object, _db, Resolver(), As("Admin"), _clock.Object);

        var act = () => handler.Handle(new DeleteFileCommand(42), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _fileRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_allows_a_file_held_only_by_a_superseded_attestation()
    {
        var file = Stored("jobs", uploadedById: CallerId);
        _db.Attestations.Add(new Attestation { SalesOrderId = 3, Status = AcceptanceStatus.Superseded, FileAttachmentId = 42 });
        await _db.SaveChangesAsync();
        var handler = new DeleteFileHandler(_fileRepo.Object, _db, Resolver(), As("Engineer"), _clock.Object);

        await handler.Handle(new DeleteFileCommand(42), CancellationToken.None);

        file.DeletedAt.Should().NotBeNull();
    }
}
