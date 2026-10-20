using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Persistence;

/// <summary>
/// Pins what <c>AppDbContext</c> writes to <c>audit_log_entries</c> on save: rows created or
/// removed by system work (no signed-in actor) are attributed to user 0 instead of being
/// skipped, hard deletes leave a trail, document lines are logged under their parent document,
/// outbox plumbing is not audited, and secrets never reach the audit payload.
/// </summary>
public class AuditCaptureCoverageTests
{
    private static async Task<SalesOrder> AddOrderAsync(AppDbContext db, params SalesOrderLine[] lines)
    {
        var order = new SalesOrder { OrderNumber = "SO-00001", CustomerId = 1 };
        foreach (var line in lines) order.Lines.Add(line);
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    [Fact]
    public async Task Rows_created_without_an_actor_are_audited_as_the_system_user()
    {
        await using var db = TestDbContextFactory.Create();
        db.CurrentUserId = null;

        var order = await AddOrderAsync(db);

        var audit = await db.AuditLogEntries.SingleAsync(a => a.Action == "SalesOrderCreated");
        audit.UserId.Should().Be(0);
        audit.EntityType.Should().Be(nameof(SalesOrder));
        audit.EntityId.Should().Be(order.Id);
        audit.Details.Should().Contain("SO-00001");
    }

    [Fact]
    public async Task Soft_deletes_without_an_actor_are_audited()
    {
        await using var db = TestDbContextFactory.Create();
        var order = await AddOrderAsync(db);

        order.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        (await db.AuditLogEntries.SingleAsync(a => a.Action == "SalesOrderDeleted")).EntityId.Should().Be(order.Id);
    }

    [Fact]
    public async Task Updates_made_without_an_actor_are_audited_as_the_system_user()
    {
        await using var db = TestDbContextFactory.Create();
        db.CurrentUserId = null;
        var order = await AddOrderAsync(db);

        order.Status = SalesOrderStatus.Cancelled;
        await db.SaveChangesAsync();

        var audit = await db.AuditLogEntries.SingleAsync(a => a.Action == "SalesOrderUpdated");
        audit.UserId.Should().Be(0);
        audit.EntityId.Should().Be(order.Id);
        audit.Details.Should().Contain("Status");
    }

    [Fact]
    public async Task Encrypted_bank_details_are_redacted_in_snapshots_and_history()
    {
        await using var db = TestDbContextFactory.Create();
        db.CurrentUserId = 2;
        var account = new VendorBankAccount
        {
            VendorId = 1, Nickname = "Operating", ChangedByUserId = 2,
            RoutingNumberEncrypted = "CfDJ8routing", AccountNumberEncrypted = "CfDJ8account-1",
            RoutingNumberMasked = "*****6789", AccountNumberMasked = "****4321",
        };
        db.VendorBankAccounts.Add(account);
        await db.SaveChangesAsync();

        account.AccountNumberEncrypted = "CfDJ8account-2";
        await db.SaveChangesAsync();

        var audits = await db.AuditLogEntries.Where(a => a.EntityType == nameof(VendorBankAccount)).ToListAsync();
        audits.Should().HaveCount(2).And.OnlyContain(a => !a.Details!.Contains("CfDJ8") && a.Details.Contains("[redacted]"));
        audits.Should().Contain(a => a.Details!.Contains("****4321"));
        var history = await db.ActivityLogs.SingleAsync(l => l.Action == "FieldChanged");
        history.OldValue.Should().Be("[redacted]");
        history.NewValue.Should().Be("[redacted]");
    }

    [Fact]
    public async Task Hard_deleting_an_auditable_row_leaves_a_deleted_trail()
    {
        await using var db = TestDbContextFactory.Create();
        db.CurrentUserId = 7;
        var setting = new SystemSetting { Key = "company.name", Value = "Example Co" };
        db.SystemSettings.Add(setting);
        await db.SaveChangesAsync();
        var id = setting.Id;

        db.SystemSettings.Remove(setting);
        await db.SaveChangesAsync();

        var audit = await db.AuditLogEntries.SingleAsync(a => a.Action == "SystemSettingDeleted");
        audit.EntityId.Should().Be(id);
        audit.UserId.Should().Be(7);
        audit.Details.Should().Contain("company.name");
        (await db.ActivityLogs.AnyAsync(l => l.EntityType == nameof(SystemSetting) && l.EntityId == id && l.Action == "Deleted"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Telemetry_tokens_written_by_the_heartbeat_are_redacted()
    {
        await using var db = TestDbContextFactory.Create();
        db.CurrentUserId = null;
        var setting = new SystemSetting { Key = "telemetry.token", Value = "tok-plaintext-123" };
        db.SystemSettings.Add(setting);
        await db.SaveChangesAsync();

        setting.Value = "tok-plaintext-456";
        db.CurrentUserId = 3;
        await db.SaveChangesAsync();

        var audits = await db.AuditLogEntries.Where(a => a.EntityType == nameof(SystemSetting)).ToListAsync();
        audits.Select(a => a.Action).Should().BeEquivalentTo("SystemSettingCreated", "SystemSettingUpdated");
        audits.Should().OnlyContain(a => !a.Details!.Contains("tok-plaintext") && a.Details.Contains("[redacted]"));
        var history = await db.ActivityLogs.Where(l => l.Action == "FieldChanged").ToListAsync();
        history.Should().ContainSingle().Which.NewValue.Should().Be("[redacted]");
    }

    [Fact]
    public async Task Ordinary_setting_values_stay_readable()
    {
        await using var db = TestDbContextFactory.Create();
        db.SystemSettings.Add(new SystemSetting { Key = "company.name", Value = "Example Co" });
        await db.SaveChangesAsync();

        (await db.AuditLogEntries.SingleAsync()).Details.Should().Contain("Example Co");
    }

    [Fact]
    public async Task Api_key_hashes_never_reach_the_audit_payload()
    {
        await using var db = TestDbContextFactory.Create();
        db.CurrentUserId = 1;
        db.SystemApiKeys.Add(new SystemApiKey { Name = "Integration", KeyHash = "AQAAAAhash", KeyPrefix = "fsk_abcdefgh", UserId = 2 });
        await db.SaveChangesAsync();

        var details = (await db.AuditLogEntries.SingleAsync(a => a.Action == "SystemApiKeyCreated")).Details;
        details.Should().NotContain("AQAAAAhash").And.Contain("[redacted]").And.Contain("fsk_abcdefgh");
    }

    [Fact]
    public async Task Line_edits_are_logged_under_the_parent_order()
    {
        await using var db = TestDbContextFactory.Create();
        db.CurrentUserId = 4;
        var line = new SalesOrderLine { Description = "Bracket", Quantity = 2, UnitPrice = 10m, LineNumber = 1 };
        var order = await AddOrderAsync(db, line);

        line.Quantity = 5;
        await db.SaveChangesAsync();

        var created = await db.AuditLogEntries.SingleAsync(a => a.Action == "SalesOrderLineCreated");
        created.EntityType.Should().Be(nameof(SalesOrder));
        created.EntityId.Should().Be(order.Id);
        created.Details.Should().Contain($"\"lineId\":{line.Id}");

        var updated = await db.AuditLogEntries.SingleAsync(a => a.Action == "SalesOrderLineUpdated");
        updated.EntityType.Should().Be(nameof(SalesOrder));
        updated.EntityId.Should().Be(order.Id);
        updated.Details.Should().Contain($"\"lineId\":{line.Id}").And.Contain("Quantity");
    }

    [Fact]
    public async Task Removing_a_line_is_logged_under_the_parent_order()
    {
        await using var db = TestDbContextFactory.Create();
        db.CurrentUserId = 4;
        var line = new SalesOrderLine { Description = "Bracket", Quantity = 2, UnitPrice = 10m, LineNumber = 1 };
        var order = await AddOrderAsync(db, line);

        db.SalesOrderLines.Remove(line);
        await db.SaveChangesAsync();

        var removed = await db.AuditLogEntries.SingleAsync(a => a.Action == "SalesOrderLineDeleted");
        removed.EntityType.Should().Be(nameof(SalesOrder));
        removed.EntityId.Should().Be(order.Id);
    }

    [Fact]
    public async Task Exchange_rates_are_audited()
    {
        await using var db = TestDbContextFactory.Create();
        db.CurrentUserId = 9;
        db.ExchangeRates.Add(new ExchangeRate
        {
            FromCurrencyId = 1, ToCurrencyId = 2, Rate = 1.25m,
            EffectiveDate = new DateOnly(2026, 10, 1), Source = ExchangeRateSource.Manual,
        });
        await db.SaveChangesAsync();

        (await db.AuditLogEntries.SingleAsync(a => a.Action == "ExchangeRateCreated")).UserId.Should().Be(9);
    }

    [Fact]
    public async Task Outbox_rows_are_not_audited()
    {
        await using var db = TestDbContextFactory.Create();
        db.CurrentUserId = 1;
        db.IntegrationOutboxEntries.Add(new IntegrationOutboxEntry
        {
            OperationKey = "email.send", IdempotencyKey = "k-1", Payload = "{\"code\":\"INVITE-123\"}",
        });
        await db.SaveChangesAsync();

        (await db.AuditLogEntries.AnyAsync()).Should().BeFalse();
    }
}
