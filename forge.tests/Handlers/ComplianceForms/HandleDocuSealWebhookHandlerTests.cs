using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

using Forge.Api.Features.ComplianceForms;
using Forge.Api.Features.EmployeeProfile;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.ComplianceForms;

public class HandleDocuSealWebhookHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EmployeeSigned = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EmployerSigned = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IDocumentSigningService> _signing = new();
    private readonly Mock<IStorageService> _storage = new();
    private readonly Mock<IMediator> _mediator = new();

    public HandleDocuSealWebhookHandlerTests()
    {
        _signing.Setup(s => s.GetSignedPdfAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([1, 2, 3]);
    }

    private HandleDocuSealWebhookHandler Handler()
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        return new HandleDocuSealWebhookHandler(
            _db, _signing.Object, _storage.Object, Options.Create(new MinioOptions()), _mediator.Object,
            clock.Object, NullLogger<HandleDocuSealWebhookHandler>.Instance);
    }

    private async Task<ComplianceFormSubmission> SeedAsync(ComplianceFormType type, int storedDocuSealId)
    {
        var template = new ComplianceFormTemplate
        {
            Name = type.ToString(),
            FormType = type,
            Description = "",
            Icon = "icon",
            IsActive = true,
            ProfileCompletionKey = type == ComplianceFormType.I9 ? "i9" : "w4",
            FormDefinitionVersions = [],
        };
        _db.ComplianceFormTemplates.Add(template);
        await _db.SaveChangesAsync();
        var submission = new ComplianceFormSubmission
        {
            TemplateId = template.Id,
            UserId = 42,
            DocuSealSubmissionId = storedDocuSealId,
            Status = ComplianceSubmissionStatus.Pending,
            FormDataJson = "{}",
        };
        _db.ComplianceFormSubmissions.Add(submission);
        await _db.SaveChangesAsync();
        return submission;
    }

    [Fact]
    public async Task I9_EmployeeFormCompleted_SetsOnlySection1()
    {
        var submission = await SeedAsync(ComplianceFormType.I9, 501);

        await Handler().Handle(
            new HandleDocuSealWebhookCommand(501, "completed", EmployeeSigned, IsSubmitterEvent: true, SubmitterId: 9001),
            CancellationToken.None);

        submission.I9Section1SignedAt.Should().Be(EmployeeSigned);
        submission.I9Section2SignedAt.Should().BeNull();
        submission.Status.Should().Be(ComplianceSubmissionStatus.Opened);
        _signing.Verify(s => s.GetSignedPdfAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _mediator.Verify(m => m.Send(It.IsAny<AcknowledgeFormCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task I9_EmployeeFormCompletedRedelivered_DoesNotCompleteTheForm()
    {
        var submission = await SeedAsync(ComplianceFormType.I9, 501);
        var handler = Handler();
        var employeeEvent = new HandleDocuSealWebhookCommand(501, "completed", EmployeeSigned, IsSubmitterEvent: true, SubmitterId: 9001);

        await handler.Handle(employeeEvent, CancellationToken.None);
        await handler.Handle(employeeEvent, CancellationToken.None);

        submission.I9Section1SignedAt.Should().Be(EmployeeSigned);
        submission.I9Section2SignedAt.Should().BeNull();
        submission.Status.Should().Be(ComplianceSubmissionStatus.Opened);
        _signing.Verify(s => s.GetSignedPdfAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task I9_EmployerFormCompleted_CompletesAndFetchesThePdfBySubmissionId()
    {
        var submission = await SeedAsync(ComplianceFormType.I9, 501);
        submission.I9Section1SignedAt = EmployeeSigned;
        await _db.SaveChangesAsync();

        await Handler().Handle(
            new HandleDocuSealWebhookCommand(501, "completed", EmployerSigned, true, SubmitterId: 9002, AllSigned: true),
            CancellationToken.None);

        submission.I9Section1SignedAt.Should().Be(EmployeeSigned);
        submission.I9Section2SignedAt.Should().Be(EmployerSigned);
        submission.Status.Should().Be(ComplianceSubmissionStatus.Completed);
        submission.SignedPdfFileId.Should().NotBeNull();
        _signing.Verify(s => s.GetSignedPdfAsync(501, It.IsAny<CancellationToken>()), Times.Once);
        _mediator.Verify(m => m.Send(It.Is<AcknowledgeFormCommand>(c => c.UserId == 42 && c.FormType == "i9"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task I9_SubmissionCompletedAfterFormCompleted_DoesNotFetchTwice()
    {
        var submission = await SeedAsync(ComplianceFormType.I9, 501);
        submission.I9Section1SignedAt = EmployeeSigned;
        await _db.SaveChangesAsync();
        var handler = Handler();

        await handler.Handle(new HandleDocuSealWebhookCommand(501, "completed", EmployerSigned, true, 9002, true), CancellationToken.None);
        await handler.Handle(new HandleDocuSealWebhookCommand(501, "completed", EmployerSigned, false, null, true), CancellationToken.None);

        _signing.Verify(s => s.GetSignedPdfAsync(501, It.IsAny<CancellationToken>()), Times.Once);
        _db.Set<FileAttachment>().Count(f => f.EntityId == submission.Id).Should().Be(1);
    }

    [Fact]
    public async Task I9_OnlySubmissionCompleted_StampsBothSections()
    {
        var submission = await SeedAsync(ComplianceFormType.I9, 501);

        await Handler().Handle(new HandleDocuSealWebhookCommand(501, "completed", EmployerSigned, false, null, true), CancellationToken.None);

        submission.I9Section1SignedAt.Should().Be(EmployerSigned);
        submission.I9Section2SignedAt.Should().Be(EmployerSigned);
        submission.Status.Should().Be(ComplianceSubmissionStatus.Completed);
    }

    [Fact]
    public async Task W4_SingleSignerFormCompleted_CompletesTheForm()
    {
        var submission = await SeedAsync(ComplianceFormType.W4, 600);

        await Handler().Handle(new HandleDocuSealWebhookCommand(600, "completed", EmployeeSigned, true, 9100, true), CancellationToken.None);

        submission.Status.Should().Be(ComplianceSubmissionStatus.Completed);
        submission.SignedAt.Should().Be(EmployeeSigned);
        _signing.Verify(s => s.GetSignedPdfAsync(600, It.IsAny<CancellationToken>()), Times.Once);
        _mediator.Verify(m => m.Send(It.Is<AcknowledgeFormCommand>(c => c.FormType == "w4"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RowStoredWithSubmitterId_MatchesBySubmitterAndHealsToTheSubmissionId()
    {
        var submission = await SeedAsync(ComplianceFormType.I9, 9001);

        await Handler().Handle(new HandleDocuSealWebhookCommand(501, "completed", EmployeeSigned, true, 9001), CancellationToken.None);

        submission.I9Section1SignedAt.Should().Be(EmployeeSigned);
        submission.DocuSealSubmissionId.Should().Be(501);
    }

    [Fact]
    public async Task Declined_SetsDeclined()
    {
        var submission = await SeedAsync(ComplianceFormType.W4, 600);

        await Handler().Handle(new HandleDocuSealWebhookCommand(600, "declined", null, true, 9100), CancellationToken.None);

        submission.Status.Should().Be(ComplianceSubmissionStatus.Declined);
    }

    [Fact]
    public async Task UnknownSubmission_IsIgnored()
    {
        await SeedAsync(ComplianceFormType.W4, 600);

        var act = () => Handler().Handle(new HandleDocuSealWebhookCommand(777, "completed", null, false, null, true), CancellationToken.None);

        await act.Should().NotThrowAsync();
        _signing.Verify(s => s.GetSignedPdfAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
