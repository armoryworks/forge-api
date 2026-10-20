using System.Security.Claims;
using System.Text;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;

using Forge.Api.Authorization;
using Forge.Api.Features.Files;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Files;

/// <summary>
/// The single-upload path had no tests, which is how `sales-orders` went
/// missing from its entity-type whitelist while the UI shipped a Documents tab
/// posting exactly that type — every upload on the tab 400'd (external QA
/// report). These pin the whitelist, the single/chunked parity, and bucket
/// routing for the sales- and purchasing-document types.
/// </summary>
public class UploadFileHandlerTests
{
    private readonly Mock<IStorageService> _storage = new();
    private readonly Mock<IFileRepository> _fileRepo = new();
    private readonly Mock<IHttpContextAccessor> _httpContextAccessor = new();
    private readonly Mock<IEntityAccessResolver> _access = new();
    private readonly MinioOptions _minio = new()
    {
        JobFilesBucket = "forge-job-files",
        ReceiptsBucket = "forge-receipts",
        EmployeeDocsBucket = "forge-employee-docs",
    };
    private readonly UploadFileHandler _handler;

    public UploadFileHandlerTests()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "7")], "Test"));
        _httpContextAccessor.Setup(a => a.HttpContext)
            .Returns(new DefaultHttpContext { User = principal });

        _fileRepo.Setup(r => r.AddAsync(It.IsAny<FileAttachment>(), It.IsAny<CancellationToken>()))
            .Callback<FileAttachment, CancellationToken>((a, _) => a.Id = 42)
            .Returns(Task.CompletedTask);
        _fileRepo.Setup(r => r.GetByEntityAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string et, int id, CancellationToken _) =>
            [
                new FileAttachmentResponseModel(
                    42, "test.png", "image/png", 3, "/api/v1/files/42/download",
                    et, id, 7, "Tester, QA", DateTimeOffset.UtcNow, null, null),
            ]);

        _access.Setup(a => a.CanAccessAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<ClaimsPrincipal?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _handler = new UploadFileHandler(
            _storage.Object, _fileRepo.Object, _httpContextAccessor.Object, Options.Create(_minio), _access.Object);
    }

    private static IFormFile PngFile() => NamedFile("test.png", "image/png");

    private static IFormFile NamedFile(string fileName, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes("bytes");
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    [Theory]
    [InlineData("sales-orders")]
    [InlineData("customers")]
    [InlineData("quotes")]
    [InlineData("purchase-orders")]
    [InlineData("lots")]
    public void Validator_accepts_sales_and_purchasing_document_entity_types(string entityType)
    {
        var result = new UploadFileCommandValidator()
            .Validate(new UploadFileCommand(entityType, 3, PngFile()));

        result.IsValid.Should().BeTrue(
            $"'{entityType}' uploads previously 400'd with 'Invalid entity type' — the reported .png failure");
    }

    [Fact]
    public void Validator_rejects_unknown_entity_type()
    {
        var result = new UploadFileCommandValidator()
            .Validate(new UploadFileCommand("nonsense", 3, PngFile()));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "Invalid entity type.");
    }

    [Fact]
    public void Single_and_chunked_validators_share_one_whitelist()
    {
        // The two whitelists used to be duplicated and drifted; both validators
        // must accept exactly the shared set.
        foreach (var entityType in FileEntityTypes.Valid)
        {
            new UploadFileCommandValidator()
                .Validate(new UploadFileCommand(entityType, 1, NamedFile("test.pdf", "application/pdf")))
                .IsValid.Should().BeTrue($"single upload must accept '{entityType}'");
        }
    }

    [Theory]
    [InlineData("sales-orders", "forge-job-files")]
    [InlineData("customers", "forge-job-files")]
    [InlineData("quotes", "forge-job-files")]
    [InlineData("purchase-orders", "forge-job-files")]
    [InlineData("lots", "forge-job-files")]
    [InlineData("expenses", "forge-receipts")]
    [InlineData("employee-docs", "forge-employee-docs")]
    public void Buckets_route_per_entity_type(string entityType, string expectedBucket)
    {
        FileEntityTypes.ResolveBucket(entityType, _minio).Should().Be(expectedBucket);
    }

    [Fact]
    public async Task Handle_uploads_to_resolved_bucket_and_persists_attachment()
    {
        var result = await _handler.Handle(
            new UploadFileCommand("sales-orders", 17, PngFile()), CancellationToken.None);

        _storage.Verify(s => s.UploadAsync(
            "forge-job-files",
            It.Is<string>(k => k.StartsWith("sales-orders/17/")),
            It.IsAny<Stream>(), "image/png", It.IsAny<CancellationToken>()), Times.Once);

        _fileRepo.Verify(r => r.AddAsync(It.Is<FileAttachment>(a =>
            a.EntityType == "sales-orders" && a.EntityId == 17 && a.UploadedById == 7),
            It.IsAny<CancellationToken>()), Times.Once);

        result.Id.Should().Be(42);
    }

    [Fact]
    public async Task Handle_accepts_a_purchase_order_attachment_into_the_job_files_bucket()
    {
        var command = new UploadFileCommand("purchase-orders", 23, PngFile());
        new UploadFileCommandValidator().Validate(command).IsValid.Should().BeTrue();

        await _handler.Handle(command, CancellationToken.None);

        _storage.Verify(s => s.UploadAsync(
            "forge-job-files",
            It.Is<string>(k => k.StartsWith("purchase-orders/23/")),
            It.IsAny<Stream>(), "image/png", It.IsAny<CancellationToken>()), Times.Once);
        _fileRepo.Verify(r => r.AddAsync(It.Is<FileAttachment>(a =>
            a.EntityType == "purchase-orders" && a.EntityId == 23 && a.BucketName == "forge-job-files"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("parts", "payload.exe")]
    [InlineData("jobs", "macro.PS1")]
    [InlineData("employee-docs", "invoice.pdf.exe")]
    [InlineData("parts", "trailing.exe. ")]
    [InlineData("customers", "page.html")]
    public void Validator_rejects_executable_and_script_files(string entityType, string fileName)
    {
        var result = new UploadFileCommandValidator()
            .Validate(new UploadFileCommand(entityType, 3, NamedFile(fileName, "application/octet-stream")));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "This file type cannot be uploaded here.");
    }

    [Theory]
    [InlineData("parts", "bracket.step")]
    [InlineData("parts", "bracket.STL")]
    [InlineData("jobs", "program.nc")]
    [InlineData("jobs", "O1001")]
    [InlineData("employee-docs", "offer.docx")]
    public void Validator_accepts_cad_cam_and_office_files_on_general_types(string entityType, string fileName)
        => new UploadFileCommandValidator()
            .Validate(new UploadFileCommand(entityType, 3, NamedFile(fileName, "application/octet-stream")))
            .IsValid.Should().BeTrue();

    [Theory]
    [InlineData("pay-stubs", "stub.pdf", true)]
    [InlineData("pay-stubs", "stub.png", false)]
    [InlineData("tax-documents", "w2.docx", false)]
    [InlineData("identity-docs", "passport.JPG", true)]
    [InlineData("identity-docs", "passport.zip", false)]
    [InlineData("identity-docs", "passport", false)]
    public void Validator_limits_hr_document_types(string entityType, string fileName, bool valid)
        => new UploadFileCommandValidator()
            .Validate(new UploadFileCommand(entityType, 3, NamedFile(fileName, "application/pdf")))
            .IsValid.Should().Be(valid);

    [Fact]
    public async Task Handle_strips_paths_from_the_name_and_normalises_the_content_type()
    {
        await _handler.Handle(
            new UploadFileCommand("parts", 5, NamedFile("..\\..\\etc/pass wd?.step", "bogus type")), CancellationToken.None);

        _storage.Verify(s => s.UploadAsync(
            "forge-job-files",
            It.Is<string>(k => k.StartsWith("parts/5/") && k.EndsWith("-pass_wd_.step") && !k.Contains("..")),
            It.IsAny<Stream>(), "application/octet-stream", It.IsAny<CancellationToken>()), Times.Once);
        _fileRepo.Verify(r => r.AddAsync(It.Is<FileAttachment>(a =>
            a.FileName == "pass wd?.step" && a.ContentType == "application/octet-stream"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
