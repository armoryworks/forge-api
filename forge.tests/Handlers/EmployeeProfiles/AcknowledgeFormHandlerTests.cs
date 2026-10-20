using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.EmployeeProfile;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.EmployeeProfiles;

public class AcknowledgeFormHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private AcknowledgeFormHandler Handler() => new(_db);

    [Theory]
    [InlineData("w4")]
    [InlineData("i9")]
    [InlineData("state_withholding")]
    [InlineData("stateWithholding")]
    [InlineData("directDeposit")]
    public async Task SelfService_SubmissionBackedForm_IsRejectedAndNothingIsRecorded(string formType)
    {
        var act = () => Handler().Handle(new AcknowledgeFormCommand(7, formType, SelfService: true), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        (await _db.EmployeeProfiles.AnyAsync(p => p.UserId == 7)).Should().BeFalse();
    }

    [Theory]
    [InlineData("handbook")]
    [InlineData("workersComp")]
    [InlineData("workers_comp")]
    public async Task SelfService_AcknowledgementForm_IsRecorded(string formType)
    {
        await Handler().Handle(new AcknowledgeFormCommand(8, formType, SelfService: true), CancellationToken.None);

        var profile = await _db.EmployeeProfiles.SingleAsync(p => p.UserId == 8);
        (profile.HandbookAcknowledgedAt ?? profile.WorkersCompAcknowledgedAt).Should().NotBeNull();
        profile.W4CompletedAt.Should().BeNull();
        profile.I9CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task SystemCaller_SubmissionBackedForm_IsRecorded()
    {
        await Handler().Handle(new AcknowledgeFormCommand(9, "i9"), CancellationToken.None);

        var profile = await _db.EmployeeProfiles.SingleAsync(p => p.UserId == 9);
        profile.I9CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UnknownFormType_ThrowsValidationException()
    {
        var act = () => Handler().Handle(new AcknowledgeFormCommand(10, "bogus"), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainSingle(e => e.PropertyName == nameof(AcknowledgeFormCommand.FormType));
    }
}
