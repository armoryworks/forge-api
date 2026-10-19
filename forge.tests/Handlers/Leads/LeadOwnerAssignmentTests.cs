using FluentAssertions;
using FluentValidation;
using Moq;
using Forge.Api.Features.Leads;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Leads;

public class LeadOwnerAssignmentTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private async Task<ApplicationUser> AddUserAsync(string first, bool active = true)
    {
        var user = new ApplicationUser
        {
            UserName = $"{first}@t.com", Email = $"{first}@t.com",
            FirstName = first, LastName = "Rep", IsActive = active,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task<List<Lead>> AddLeadsAsync(int count, int? accountId = null)
    {
        var leads = Enumerable.Range(1, count)
            .Select(i => new Lead { CompanyName = $"Prospect {i}", Status = LeadStatus.New, CreatedBy = 1, AccountId = accountId })
            .ToList();
        _db.Leads.AddRange(leads);
        await _db.SaveChangesAsync();
        return leads;
    }

    [Fact]
    public async Task Bulk_assign_sets_the_owner_on_every_selected_lead_and_keeps_the_account()
    {
        var owen = await AddUserAsync("Owen");
        var account = new Account { Name = "Fluid Power Prospects" };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        var leads = await AddLeadsAsync(6, account.Id);

        var updated = await new AssignLeadsOwnerHandler(_db)
            .Handle(new AssignLeadsOwnerCommand(leads.Select(l => l.Id).ToList(), owen.Id), CancellationToken.None);

        updated.Should().Be(6);
        _db.Leads.Should().OnlyContain(l => l.AssignedToUserId == owen.Id && l.AccountId == account.Id);
        _db.ActivityLogs.Count(a => a.Description == "Updated 1 field: owner: Owen Rep").Should().Be(6);
    }

    [Fact]
    public async Task Bulk_assign_with_zero_returns_the_leads_to_the_open_queue()
    {
        var owen = await AddUserAsync("Owen");
        var leads = await AddLeadsAsync(2);
        leads.ForEach(l => l.AssignedToUserId = owen.Id);
        await _db.SaveChangesAsync();

        await new AssignLeadsOwnerHandler(_db)
            .Handle(new AssignLeadsOwnerCommand(leads.Select(l => l.Id).ToList(), 0), CancellationToken.None);

        _db.Leads.Should().OnlyContain(l => l.AssignedToUserId == null);
    }

    [Fact]
    public async Task Assigning_to_an_inactive_user_is_refused()
    {
        var gone = await AddUserAsync("Gone", active: false);
        var leads = await AddLeadsAsync(1);

        var act = () => new AssignLeadsOwnerHandler(_db)
            .Handle(new AssignLeadsOwnerCommand([leads[0].Id], gone.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        _db.Leads.Single().AssignedToUserId.Should().BeNull();
    }

    [Fact]
    public async Task Update_lead_sets_the_owner()
    {
        var owen = await AddUserAsync("Owen");
        var lead = (await AddLeadsAsync(1))[0];
        var repo = new Mock<ILeadRepository>();
        repo.Setup(r => r.FindAsync(lead.Id, It.IsAny<CancellationToken>())).ReturnsAsync(lead);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(() => _db.SaveChangesAsync());
        repo.Setup(r => r.GetByIdAsync(lead.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeadResponseModel(
                lead.Id, lead.CompanyName, null, null, null, null,
                LeadStatus.New, null, null, null, null, DateTime.UtcNow, DateTime.UtcNow));
        var handler = new UpdateLeadHandler(
            repo.Object, Mock.Of<ISystemSettingRepository>(), Mock.Of<IBusinessIdentifierService>(), _db);

        await handler.Handle(
            new UpdateLeadCommand(lead.Id, new UpdateLeadRequestModel(
                null, null, null, null, null, null, null, null, null, AssignedToUserId: owen.Id)),
            CancellationToken.None);

        lead.AssignedToUserId.Should().Be(owen.Id);
    }
}
