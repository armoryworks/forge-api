using FluentAssertions;
using Moq;
using Forge.Api.Features.Leads;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Leads;

public class UpdateLeadNumberTests
{
    private readonly Mock<ILeadRepository> _leadRepo = new();
    private readonly Mock<ISystemSettingRepository> _settings = new();
    private readonly Mock<IBusinessIdentifierService> _identifiers = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly UpdateLeadHandler _handler;

    public UpdateLeadNumberTests()
    {
        _identifiers.Setup(i => i.IssueAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());
        _identifiers.Setup(i => i.RenameAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());

        _handler = new UpdateLeadHandler(
            _leadRepo.Object, _settings.Object, _identifiers.Object, _db);
    }

    private void AllowManualNumbers(bool allowed) =>
        _settings.Setup(s => s.FindByKeyAsync("leads.allow_manual_numbers", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSetting { Key = "leads.allow_manual_numbers", Value = allowed ? "true" : "false" });

    private void SetupRepoForUpdate(Lead lead)
    {
        _leadRepo.Setup(r => r.FindAsync(lead.Id, It.IsAny<CancellationToken>())).ReturnsAsync(lead);
        _leadRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _leadRepo.Setup(r => r.GetByIdAsync(lead.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeadResponseModel(
                lead.Id, lead.CompanyName, null, null, null, null,
                LeadStatus.New, null, null, null, null, DateTime.UtcNow, DateTime.UtcNow));
    }

    private static UpdateLeadCommand WithLeadNumber(int id, string number) =>
        new(id, new UpdateLeadRequestModel(null, null, null, null, null, null, null, null, null, LeadNumber: number));

    [Fact]
    public async Task Renames_the_lead_number_when_manual_numbers_allowed_and_unique()
    {
        var lead = new Lead { Id = 1, CompanyName = "Acme", LeadNumber = "LEAD-00001", Status = LeadStatus.New };
        SetupRepoForUpdate(lead);
        AllowManualNumbers(true);
        _leadRepo.Setup(r => r.LeadNumberExistsAsync("ACME-42", 1, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await _handler.Handle(WithLeadNumber(1, "ACME-42"), CancellationToken.None);

        lead.LeadNumber.Should().Be("ACME-42");
        _identifiers.Verify(i => i.RenameAsync(BusinessEntityType.Lead, 1, "ACME-42", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rejects_a_duplicate_lead_number()
    {
        var lead = new Lead { Id = 1, CompanyName = "Acme", LeadNumber = "LEAD-00001", Status = LeadStatus.New };
        SetupRepoForUpdate(lead);
        AllowManualNumbers(true);
        _leadRepo.Setup(r => r.LeadNumberExistsAsync("TAKEN", 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => _handler.Handle(WithLeadNumber(1, "TAKEN"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("already in use");
        lead.LeadNumber.Should().Be("LEAD-00001");
    }

    [Fact]
    public async Task Rejects_a_rename_when_manual_numbers_disabled()
    {
        var lead = new Lead { Id = 1, CompanyName = "Acme", LeadNumber = "LEAD-00001", Status = LeadStatus.New };
        SetupRepoForUpdate(lead);
        AllowManualNumbers(false);

        var act = () => _handler.Handle(WithLeadNumber(1, "ACME-42"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("disabled");
        lead.LeadNumber.Should().Be("LEAD-00001");
    }
}
