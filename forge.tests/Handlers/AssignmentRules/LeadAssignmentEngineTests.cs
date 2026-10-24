using FluentAssertions;
using Moq;

using Forge.Api.Features.AssignmentRules;
using Forge.Api.Features.Leads;
using Forge.Api.Features.Leads.BulkIntake;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.AssignmentRules;

public class LeadAssignmentEngineTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private async Task<int> AddUserAsync(string first, bool active = true)
    {
        var user = new ApplicationUser
        {
            UserName = $"{first}@t.com", Email = $"{first}@t.com", FirstName = first, LastName = "Rep", IsActive = active,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<AssignmentRule> AddRuleAsync(string name, AssignmentRuleKind kind, int priority, string? spec, bool active = true)
    {
        var rule = new AssignmentRule { Name = name, Kind = kind, Priority = priority, Spec = spec, IsActive = active };
        _db.AssignmentRules.Add(rule);
        await _db.SaveChangesAsync();
        return rule;
    }

    private static Lead NewLead(string company, int? accountId = null) =>
        new() { CompanyName = company, Status = LeadStatus.New, CreatedBy = 1, AccountId = accountId };

    [Fact]
    public async Task Round_robin_rotates_through_active_reps_and_keeps_its_cursor_between_calls()
    {
        var owen = await AddUserAsync("Owen");
        var inactive = await AddUserAsync("Ivy", active: false);
        var priya = await AddUserAsync("Priya");
        var rule = await AddRuleAsync("Round robin", AssignmentRuleKind.RoundRobin, 10, $"{{\"repIds\":[{owen},{inactive},{priya}]}}");

        var first = new[] { NewLead("A"), NewLead("B"), NewLead("C") };
        await LeadAssignmentEngine.AssignAsync(_db, first, CancellationToken.None);
        await _db.SaveChangesAsync();
        var second = new[] { NewLead("D") };
        await LeadAssignmentEngine.AssignAsync(_db, second, CancellationToken.None);

        first.Select(l => l.AssignedToUserId).Should().Equal(owen, priya, owen);
        second.Single().AssignedToUserId.Should().Be(priya);
        rule.LastAssignedUserId.Should().Be(priya);
    }

    [Fact]
    public async Task Rules_run_in_priority_order_and_the_first_match_wins()
    {
        var west = await AddUserAsync("West");
        var metal = await AddUserAsync("Metal");
        var anyone = await AddUserAsync("Anyone");
        _db.Accounts.AddRange(
            new Account { Id = 1, Name = "Utah Co", State = "UT", PostalCode = "84101", Industry = "Aerospace" },
            new Account { Id = 2, Name = "Ohio Co", State = "OH", PostalCode = "43004", Industry = "Metalworking" },
            new Account { Id = 3, Name = "Zip Co", State = null, PostalCode = "84604" });
        await _db.SaveChangesAsync();
        await AddRuleAsync("Catch-all", AssignmentRuleKind.RoundRobin, 30, $"{{\"repIds\":[{anyone}]}}");
        await AddRuleAsync("Metal", AssignmentRuleKind.Industry, 20, $"{{\"industries\":[\"metalworking\"],\"repIds\":[{metal}]}}");
        await AddRuleAsync("West", AssignmentRuleKind.Territory, 10, $"{{\"states\":[\"UT\"],\"zipPrefixes\":[\"846\"],\"repIds\":[{west}]}}");
        await AddRuleAsync("Disabled", AssignmentRuleKind.RoundRobin, 1, $"{{\"repIds\":[{metal}]}}", active: false);

        var leads = new[] { NewLead("Utah", 1), NewLead("Ohio", 2), NewLead("Zip", 3), NewLead("None") };
        var byRule = await LeadAssignmentEngine.AssignAsync(_db, leads, CancellationToken.None);

        leads.Select(l => l.AssignedToUserId).Should().Equal(west, metal, west, anyone);
        byRule[leads[1]].Name.Should().Be("Metal");
    }

    [Fact]
    public async Task Account_based_rules_use_the_account_owner_then_an_earlier_lead_owner()
    {
        var owner = await AddUserAsync("Owner");
        var earlier = await AddUserAsync("Earlier");
        _db.Accounts.Add(new Account { Id = 5, Name = "Owned", OwnerUserId = owner });
        _db.Leads.Add(new Lead { CompanyName = "Repeat Corp", CreatedBy = 1, AssignedToUserId = earlier });
        await _db.SaveChangesAsync();
        await AddRuleAsync("Sticky", AssignmentRuleKind.AccountBased, 1, null);

        var leads = new[] { NewLead("Owned", 5), NewLead(" repeat corp "), NewLead("Stranger") };
        await LeadAssignmentEngine.AssignAsync(_db, leads, CancellationToken.None);

        leads.Select(l => l.AssignedToUserId).Should().Equal(owner, earlier, null);
    }

    [Fact]
    public async Task A_lead_that_already_has_an_owner_or_a_rule_with_a_bad_spec_is_left_alone()
    {
        await AddUserAsync("Rep");
        await AddRuleAsync("Broken", AssignmentRuleKind.RoundRobin, 1, "{not json");
        var owned = NewLead("Owned");
        owned.AssignedToUserId = 99;
        var open = NewLead("Open");

        await LeadAssignmentEngine.AssignAsync(_db, [owned, open], CancellationToken.None);

        owned.AssignedToUserId.Should().Be(99);
        open.AssignedToUserId.Should().BeNull();
    }

    [Fact]
    public async Task Creating_a_lead_applies_the_rules_and_logs_the_assignment()
    {
        var owen = await AddUserAsync("Owen");
        await AddRuleAsync("Round robin: Owen", AssignmentRuleKind.RoundRobin, 1, $"{{\"repIds\":[{owen}]}}");
        _db.CurrentUserId = 7;
        var repo = new Mock<ILeadRepository>();
        Lead? added = null;
        repo.Setup(r => r.AddAsync(It.IsAny<Lead>(), It.IsAny<CancellationToken>()))
            .Callback<Lead, CancellationToken>((l, _) => { _db.Leads.Add(l); _db.SaveChanges(); added = l; })
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.GenerateNextLeadNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("LEAD-00001");
        var handler = new CreateLeadHandler(
            repo.Object, Mock.Of<ISystemSettingRepository>(), Mock.Of<IBusinessIdentifierService>(), _db);

        await handler.Handle(new CreateLeadCommand(new CreateLeadRequestModel(
            "Web Form Co", "Pat", "pat@example.com", null, "Website", null, null)), CancellationToken.None);

        added!.AssignedToUserId.Should().Be(owen);
        _db.ActivityLogs.Should().ContainSingle(a => a.EntityType == "Lead" && a.EntityId == added.Id && a.Action == "assigned")
            .Which.Description.Should().Be("Assigned to Rep, Owen by rule 'Round robin: Owen'");
    }

    [Fact]
    public async Task Bulk_intake_assigns_by_rule_and_records_the_importer_as_creator()
    {
        var owen = await AddUserAsync("Owen");
        var priya = await AddUserAsync("Priya");
        await AddRuleAsync("Round robin", AssignmentRuleKind.RoundRobin, 1, $"{{\"repIds\":[{owen},{priya}]}}");
        _db.CurrentUserId = priya;
        var request = new BulkLeadIntakeRequest(BulkLeadIntakeStrategy.ManualEntry, null, null,
        [
            new BulkLeadIntakeRow("1", "One Co", null, "one@example.com", null, null, null),
            new BulkLeadIntakeRow("2", "Two Co", null, "two@example.com", null, null, null),
        ]);

        await new BulkLeadIntakeHandler(_db, Mock.Of<IClock>(c => c.UtcNow == DateTimeOffset.UtcNow))
            .Handle(new BulkLeadIntakeCommand(request, Commit: true), CancellationToken.None);

        var leads = _db.Leads.OrderBy(l => l.Id).ToList();
        leads.Select(l => l.AssignedToUserId).Should().Equal(owen, priya);
        leads.Should().OnlyContain(l => l.CreatedBy == priya);
        _db.ActivityLogs.Count(a => a.Action == "assigned").Should().Be(2);
    }
}
