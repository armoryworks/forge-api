using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Moq;

using Forge.Api.Features.Onboarding;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Onboarding;

public class SaveOnboardingDraftHandlerTests
{
    private const int EmployeeId = 10;
    private const int AdminId = 20;
    private const int OfficeManagerId = 21;
    private const int ManagerId = 22;
    private const int InactiveAdminId = 23;

    private static readonly SaveOnboardingDraftRequestModel Empty = new(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IPiiProtector> _pii = new();
    private readonly Mock<ISystemAuditWriter> _audit = new();

    public SaveOnboardingDraftHandlerTests()
    {
        _pii.Setup(p => p.Protect(It.IsAny<string?>()))
            .Returns((string? v) => string.IsNullOrWhiteSpace(v) ? null : "enc:" + v);
        _pii.Setup(p => p.Unprotect(It.IsAny<string?>()))
            .Returns((string? v) => string.IsNullOrWhiteSpace(v) ? null : v["enc:".Length..]);

        AddUser(EmployeeId, true);
        AddUser(AdminId, true, "Admin");
        AddUser(OfficeManagerId, true, "OfficeManager");
        AddUser(ManagerId, true, "Manager");
        AddUser(InactiveAdminId, false, "Admin");
        _db.SaveChanges();
    }

    private void AddUser(int id, bool active, string? role = null)
    {
        _db.Users.Add(new ApplicationUser
        {
            Id = id, UserName = $"u{id}@example.com", Email = $"u{id}@example.com",
            FirstName = "First" + id, LastName = "Last" + id, IsActive = active,
        });
        if (role is null) return;
        var existing = _db.Roles.Local.FirstOrDefault(r => r.Name == role);
        if (existing is null)
        {
            existing = new IdentityRole<int> { Id = 100 + _db.Roles.Local.Count, Name = role };
            _db.Roles.Add(existing);
        }
        _db.UserRoles.Add(new IdentityUserRole<int> { UserId = id, RoleId = existing.Id });
    }

    private EmployeeProfile SeedProfile(bool directDepositOnFile)
    {
        var profile = new EmployeeProfile
        {
            UserId = EmployeeId,
            BankName = "First Bank",
            BankAccountType = "checking",
            BankRoutingProtected = "enc:111000025",
            BankAccountProtected = "enc:123456789",
            DirectDepositCompletedAt = directDepositOnFile ? new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero) : null,
        };
        _db.EmployeeProfiles.Add(profile);
        _db.SaveChanges();
        return profile;
    }

    private Task Save(SaveOnboardingDraftRequestModel model)
        => new SaveOnboardingDraftHandler(_db, _pii.Object, _audit.Object)
            .Handle(new SaveOnboardingDraftCommand(EmployeeId, model), CancellationToken.None);

    [Fact]
    public async Task AccountChangedAfterCompletion_NotifiesPayrollAndEmployeeAndAudits()
    {
        var profile = SeedProfile(directDepositOnFile: true);

        await Save(Empty with { AccountNumber = "987654321", RoutingNumber = "111000025" });

        profile.BankAccountProtected.Should().Be("enc:987654321");
        var recipients = _db.Notifications.Where(n => n.Type == "direct_deposit_changed").Select(n => n.UserId).ToList();
        recipients.Should().BeEquivalentTo([AdminId, OfficeManagerId, EmployeeId]);
        _audit.Verify(a => a.WriteAsync("DirectDepositChanged", It.IsAny<int>(), "EmployeeProfile", profile.Id,
            It.Is<string?>(d => d!.Contains("accountNumber") && !d.Contains("routingNumber") && d.Contains("\"4321\"") && !d.Contains("987654321")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BankNameChangedAfterCompletion_IsReported()
    {
        SeedProfile(directDepositOnFile: true);

        await Save(Empty with { BankName = "Second Bank" });

        _db.Notifications.Count(n => n.Type == "direct_deposit_changed").Should().Be(3);
    }

    [Fact]
    public async Task FirstTimeEntry_SendsNothing()
    {
        SeedProfile(directDepositOnFile: false);

        await Save(Empty with { AccountNumber = "987654321" });

        _db.Notifications.Should().BeEmpty();
        _audit.Verify(a => a.WriteAsync("DirectDepositChanged", It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<int?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnchangedValuesResent_SendsNothing()
    {
        SeedProfile(directDepositOnFile: true);

        await Save(Empty with { BankName = "First Bank", AccountType = "checking", RoutingNumber = "111000025", AccountNumber = "123456789" });

        _db.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task OtherStepsAfterCompletion_SendNothing()
    {
        SeedProfile(directDepositOnFile: true);

        await Save(Empty with { City = "Ogden" });

        _db.Notifications.Should().BeEmpty();
    }
}
