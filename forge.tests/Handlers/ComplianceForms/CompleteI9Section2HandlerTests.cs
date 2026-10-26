using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

using Forge.Api.Features.ComplianceForms;
using Forge.Api.Features.EmployeeProfile;
using Forge.Api.Features.Onboarding;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.ComplianceForms;

public class CompleteI9Section2HandlerTests
{
    private const int EmployeeId = 42;
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly StubCapabilitySnapshotProvider _hrOn = new(EmployeeComplianceRules.HrTrackingCapability);

    private CompleteI9Section2Handler Handler()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<AcknowledgeFormCommand>(), It.IsAny<CancellationToken>()))
            .Returns((AcknowledgeFormCommand cmd, CancellationToken ct) => new AcknowledgeFormHandler(_db).Handle(cmd, ct));
        return new CompleteI9Section2Handler(
            _db, mediator.Object, new Mock<IDocumentSigningService>().Object,
            Options.Create(new MinioOptions()), new Mock<IStorageService>().Object);
    }

    private async Task<ComplianceFormSubmission> SeedAsync()
    {
        var template = new ComplianceFormTemplate
        {
            Name = "I-9",
            FormType = ComplianceFormType.I9,
            Description = "",
            Icon = "icon",
            IsActive = true,
            ProfileCompletionKey = "i9",
            FormDefinitionVersions = [],
        };
        _db.ComplianceFormTemplates.Add(template);
        _db.EmployeeProfiles.Add(new EmployeeProfile
        {
            UserId = EmployeeId,
            W4CompletedAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            StateWithholdingCompletedAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            EmergencyContactName = "Pat Doe",
            EmergencyContactPhone = "555-0100",
        });
        await _db.SaveChangesAsync();
        var submission = new ComplianceFormSubmission
        {
            TemplateId = template.Id,
            UserId = EmployeeId,
            Status = ComplianceSubmissionStatus.Pending,
            FormDataJson = "{}",
            I9Section1SignedAt = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero),
        };
        _db.ComplianceFormSubmissions.Add(submission);
        await _db.SaveChangesAsync();
        return submission;
    }

    [Fact]
    public async Task Completing_section_2_marks_the_i9_complete_everywhere_it_is_read()
    {
        var submission = await SeedAsync();

        await Handler().Handle(new CompleteI9Section2Command(
            submission.Id, EmployerUserId: 7, "A", "{\"listA_doc1_type\":\"US Passport\"}",
            new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero), null), CancellationToken.None);

        var profile = await _db.EmployeeProfiles.AsNoTracking().SingleAsync(p => p.UserId == EmployeeId);
        profile.I9CompletedAt.Should().NotBeNull();

        var completeness = await new GetProfileCompletenessHandler(_db, _hrOn)
            .Handle(new GetProfileCompletenessQuery(EmployeeId), CancellationToken.None);
        completeness.Items.Single(i => i.Key == "i9").IsComplete.Should().BeTrue();

        var onboarding = await new GetOnboardingStatusHandler(_db, _hrOn)
            .Handle(new GetOnboardingStatusQuery(EmployeeId), CancellationToken.None);
        onboarding.I9Complete.Should().BeTrue();

        EmployeeComplianceRules.CanBeAssignedJobs(profile, stateCategory: null, hrOn: true).Should().BeTrue();
    }

    [Fact]
    public async Task Section_1_alone_does_not_mark_the_i9_complete()
    {
        await SeedAsync();

        var onboarding = await new GetOnboardingStatusHandler(_db, _hrOn)
            .Handle(new GetOnboardingStatusQuery(EmployeeId), CancellationToken.None);

        onboarding.I9Complete.Should().BeFalse();
    }
}
