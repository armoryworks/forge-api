using FluentAssertions;
using FluentValidation;
using Moq;

using Forge.Api.Features.Payroll;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Payroll;

public class PayrollDocumentHandlerTests
{
    private static readonly DateTimeOffset PeriodStart = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PeriodEnd = new(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PayDate = new(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private async Task<(int UserId, int FileId)> SeedAsync()
    {
        _db.Users.Add(new ApplicationUser
        {
            Id = 32, UserName = "worker@example.com", Email = "worker@example.com",
            FirstName = "Pat", LastName = "Worker", IsActive = true,
        });
        var file = new FileAttachment
        {
            FileName = "stub.pdf", ContentType = "application/pdf", BucketName = "b", ObjectKey = "k",
            EntityType = "pay-stubs", EntityId = 32, UploadedById = 32,
        };
        _db.FileAttachments.Add(file);
        await _db.SaveChangesAsync();
        return (32, file.Id);
    }

    private static UploadPayStubCommand Stub(int userId, decimal gross, decimal net, int fileId = 1,
        DateTimeOffset? start = null, DateTimeOffset? end = null, DateTimeOffset? payDate = null) =>
        new(userId, new UploadPayStubRequestModel(start ?? PeriodStart, end ?? PeriodEnd, payDate ?? PayDate, gross, net, fileId));

    [Fact]
    public async Task UploadPayStub_DeductionsEqualRoundedGrossMinusRoundedNet()
    {
        var (userId, fileId) = await SeedAsync();

        var result = await new UploadPayStubHandler(_db).Handle(Stub(userId, 100.005m, 50.004m, fileId), CancellationToken.None);

        result.GrossPay.Should().Be(100.01m);
        result.NetPay.Should().Be(50.00m);
        result.TotalDeductions.Should().Be(result.GrossPay - result.NetPay);
    }

    [Fact]
    public async Task UploadPayStub_UnknownUserAndMissingFile_ThrowValidationException()
    {
        await SeedAsync();

        var act = () => new UploadPayStubHandler(_db).Handle(Stub(999999, 500m, 400m, 999999), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo("UserId", "FileAttachmentId");
        _db.PayStubs.Should().BeEmpty();
    }

    [Theory]
    [InlineData("tax-documents", 32)]
    [InlineData("pay-stubs", 77)]
    public async Task UploadPayStub_FileFromAnotherRecord_ThrowsValidationException(string entityType, int entityId)
    {
        var (userId, _) = await SeedAsync();
        var other = new FileAttachment
        {
            FileName = "other.pdf", ContentType = "application/pdf", BucketName = "b", ObjectKey = "k2",
            EntityType = entityType, EntityId = entityId, UploadedById = 32,
        };
        _db.FileAttachments.Add(other);
        await _db.SaveChangesAsync();

        var act = () => new UploadPayStubHandler(_db).Handle(Stub(userId, 500m, 400m, other.Id), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo("FileAttachmentId");
        _db.PayStubs.Should().BeEmpty();
    }

    [Fact]
    public async Task UploadTaxDocument_PayStubFile_ThrowsValidationException()
    {
        var (userId, payStubFileId) = await SeedAsync();

        var act = () => new UploadTaxDocumentHandler(_db).Handle(
            new UploadTaxDocumentCommand(userId, new UploadTaxDocumentRequestModel(TaxDocumentType.W2, 2025, payStubFileId)),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        _db.TaxDocuments.Should().BeEmpty();
    }

    [Fact]
    public void UploadPayStubValidator_RejectsImpossibleValues()
    {
        var validator = new UploadPayStubValidator();

        validator.Validate(Stub(32, 500m, 650m)).IsValid.Should().BeFalse();
        validator.Validate(Stub(32, 1_000_000_000_000m, 1m)).IsValid.Should().BeFalse();
        validator.Validate(Stub(32, -50m, -60m)).IsValid.Should().BeFalse();
        validator.Validate(Stub(32, 100.005m, 50m)).IsValid.Should().BeFalse();
        validator.Validate(Stub(32, 500m, 400m, end: PeriodStart.AddDays(-1))).IsValid.Should().BeFalse();
        validator.Validate(Stub(32, 500m, 400m, payDate: new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero))).IsValid.Should().BeFalse();
        validator.Validate(Stub(32, 1186.25m, 925.28m)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void UploadTaxDocumentValidator_RejectsFutureAndAncientTaxYears()
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero));
        var validator = new UploadTaxDocumentValidator(clock.Object);

        UploadTaxDocumentCommand Doc(int year) => new(32, new UploadTaxDocumentRequestModel(TaxDocumentType.W2, year, 1));

        validator.Validate(Doc(99999)).IsValid.Should().BeFalse();
        validator.Validate(Doc(2027)).IsValid.Should().BeFalse();
        validator.Validate(Doc(1899)).IsValid.Should().BeFalse();
        validator.Validate(Doc(2025)).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task UploadTaxDocument_UnknownUser_ThrowsValidationException()
    {
        var (_, fileId) = await SeedAsync();

        var act = () => new UploadTaxDocumentHandler(_db).Handle(
            new UploadTaxDocumentCommand(999999, new UploadTaxDocumentRequestModel(TaxDocumentType.W2, 2025, fileId)),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        _db.TaxDocuments.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPayStubPdf_AnotherUsersStub_IsNotFound()
    {
        var (userId, fileId) = await SeedAsync();
        _db.PayStubs.Add(new PayStub
        {
            UserId = userId, PayPeriodStart = PeriodStart, PayPeriodEnd = PeriodEnd, PayDate = PayDate,
            GrossPay = 1m, NetPay = 1m, FileAttachmentId = fileId, Source = PayrollDocumentSource.Manual,
        });
        await _db.SaveChangesAsync();
        var stubId = _db.PayStubs.Single().Id;

        var act = () => new GetPayStubPdfHandler(_db).Handle(new GetPayStubPdfQuery(stubId, 77, false), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        (await new GetPayStubPdfHandler(_db).Handle(new GetPayStubPdfQuery(stubId, userId, false), CancellationToken.None))
            .Should().Be(fileId);
    }

    [Fact]
    public async Task GetTaxDocumentPdf_AnotherUsersDocument_IsNotFound()
    {
        var (userId, fileId) = await SeedAsync();
        _db.TaxDocuments.Add(new TaxDocument
        {
            UserId = userId, DocumentType = TaxDocumentType.W2, TaxYear = 2025,
            FileAttachmentId = fileId, Source = PayrollDocumentSource.Manual,
        });
        await _db.SaveChangesAsync();
        var docId = _db.TaxDocuments.Single().Id;

        var act = () => new GetTaxDocumentPdfHandler(_db).Handle(new GetTaxDocumentPdfQuery(docId, 77, false), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
