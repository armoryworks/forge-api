using System.Text;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Banking;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Banking;

/// <summary>
/// BANK-002 Phase A — vendor bank accounts + payment batches. Proves:
///   • numbers are encrypted + masked (plaintext never persisted, models carry masks only);
///   • dual control — the change-maker can never self-approve; a change resets every attestation;
///   • prenote lifecycle (Approved → batch release → PrenoteSent → MarkVerified → Verified);
///   • batch eligibility excludes batched/transmitted payments and vendors without payable accounts;
///   • exposure limit blocks generation; missing origination settings block generation;
///   • SoD — the batch creator cannot release; release creates Succeeded transmissions
///     (engaging the existing no-void-after-transmit guard) or flips prenote accounts;
///   • cancel returns payments to the eligible pool.
/// </summary>
public class VendorBankingTests
{
    private const string ValidRouting = "061000104";

    private sealed class FakeProtector : IBankingDataProtector
    {
        public string? Protect(string? plaintext)
            => plaintext is null ? null : "ENC:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
        public string? Unprotect(string? ciphertext)
            => ciphertext is null ? null : Encoding.UTF8.GetString(Convert.FromBase64String(ciphertext[4..]));
    }

    private sealed class FakeSettings(Dictionary<string, string?> values) : ISettingsService
    {
        public Task<string?> GetStringAsync(string key, CancellationToken ct = default)
            => Task.FromResult(values.TryGetValue(key, out var v)
                ? v
                : SettingDescriptorCatalog.FindByKey(key)?.DefaultValue);
        public Task<bool> GetBoolAsync(string key, CancellationToken ct = default)
            => GetStringAsync(key, ct).ContinueWith(t => bool.TryParse(t.Result, out var b) && b, ct);
        public Task<int> GetIntAsync(string key, CancellationToken ct = default)
            => GetStringAsync(key, ct).ContinueWith(t => int.TryParse(t.Result, out var i) ? i : 0, ct);
        public Task SetAsync(string key, string? value, CancellationToken ct = default)
        { values[key] = value; return Task.CompletedTask; }
        public Task<IReadOnlyDictionary<string, string?>> GetGroupAsync(string group, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, string?>>(values);
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);
    }

    private static Dictionary<string, string?> ConfiguredOrigination(bool requirePrenote = true) => new()
    {
        [BankingSettings.ImmediateDestinationKey] = "071000301",
        [BankingSettings.ImmediateDestinationNameKey] = "FRONTIER CU",
        [BankingSettings.ImmediateOriginKey] = "1234567890",
        [BankingSettings.ImmediateOriginNameKey] = "ARMORY PLASTICS LLC",
        [BankingSettings.CompanyNameKey] = "ARMORY PLASTICS",
        [BankingSettings.CompanyIdKey] = "1123456789",
        [BankingSettings.OriginatingDfiKey] = "07100030",
        [BankingSettings.RequirePrenoteKey] = requirePrenote ? "true" : "false",
    };

    private static (AppDbContext Db, VendorBankAccountService Accounts, PaymentBatchService Batches, FakeSettings Settings)
        CreateHarness(Dictionary<string, string?>? settings = null)
    {
        var db = TestDbContextFactory.Create();
        var fakeSettings = new FakeSettings(settings ?? ConfiguredOrigination());
        var clock = new FakeClock();
        var protector = new FakeProtector();
        var accounts = new VendorBankAccountService(db, protector, fakeSettings, clock);
        var batches = new PaymentBatchService(db, accounts, protector, fakeSettings, clock);
        return (db, accounts, batches, fakeSettings);
    }

    private static async Task<int> AddVendorAsync(AppDbContext db, string name = "Pacific Tool Supply")
    {
        var vendor = new Vendor { CompanyName = name, IsActive = true };
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();
        return vendor.Id;
    }

    private static async Task<VendorPayment> AddAchPaymentAsync(AppDbContext db, int vendorId, decimal amount)
    {
        var payment = new VendorPayment
        {
            PaymentNumber = $"VPAY-{Guid.NewGuid().ToString()[..6]}",
            VendorId = vendorId,
            Method = PaymentMethod.BankTransfer,
            Amount = amount,
            PaymentDate = new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero),
        };
        db.VendorPayments.Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }

    private static SaveVendorBankAccountRequestModel Request(string nickname = "Operating")
        => new(nickname, "Checking", ValidRouting, "000123456789");

    /// <summary>Full happy path to a Verified payable account (create → approve → prenote → verify).</summary>
    private static async Task<VendorBankAccountModel> VerifiedAccountAsync(
        VendorBankAccountService accounts, PaymentBatchService batches, AppDbContext db, int vendorId)
    {
        var created = await accounts.CreateAsync(vendorId, Request(), userId: 1);
        await accounts.ApproveAsync(created.Id, userId: 2);
        var prenote = await batches.CreatePrenoteBatchAsync(new DateOnly(2026, 6, 15), userId: 1);
        await batches.GenerateAsync(prenote.Id, userId: 1);
        await batches.ReleaseAsync(prenote.Id, userId: 2);
        return await accounts.MarkVerifiedAsync(created.Id, userId: 1);
    }

    // ─────────────────────── Bank accounts ───────────────────────

    [Fact]
    public async Task Create_EncryptsAndMasks_NeverPersistsPlaintext()
    {
        var (db, accounts, _, _) = CreateHarness();
        var vendorId = await AddVendorAsync(db);

        var model = await accounts.CreateAsync(vendorId, Request(), userId: 1);

        model.Status.Should().Be("PendingApproval");
        model.RoutingNumberMasked.Should().EndWith("0104").And.NotContain("061000104");
        model.AccountNumberMasked.Should().EndWith("6789").And.NotContain("000123456789");

        var row = await db.VendorBankAccounts.SingleAsync();
        row.RoutingNumberEncrypted.Should().NotContain(ValidRouting);
        row.AccountNumberEncrypted.Should().NotContain("000123456789");
    }

    [Fact]
    public async Task Create_InvalidRoutingChecksum_Throws()
    {
        var (db, accounts, _, _) = CreateHarness();
        var vendorId = await AddVendorAsync(db);

        var act = () => accounts.CreateAsync(
            vendorId, new SaveVendorBankAccountRequestModel("Op", "Checking", "061000105", "12345678"), 1);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ABA checksum*");
    }

    [Fact]
    public async Task Approve_SelfApproval_Blocked_DistinctUserWorks()
    {
        var (db, accounts, _, _) = CreateHarness();
        var vendorId = await AddVendorAsync(db);
        var created = await accounts.CreateAsync(vendorId, Request(), userId: 7);

        var self = () => accounts.ApproveAsync(created.Id, userId: 7);
        await self.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Dual control*");

        var approved = await accounts.ApproveAsync(created.Id, userId: 9);
        approved.Status.Should().Be("Approved");
        approved.ApprovedByUserId.Should().Be(9);
    }

    [Fact]
    public async Task Update_ResetsApprovalAndPrenote()
    {
        var (db, accounts, batches, _) = CreateHarness();
        var vendorId = await AddVendorAsync(db);
        var verified = await VerifiedAccountAsync(accounts, batches, db, vendorId);

        var updated = await accounts.UpdateNumbersAsync(verified.Id, Request("New numbers"), userId: 3);

        updated.Status.Should().Be("PendingApproval");
        updated.ApprovedByUserId.Should().BeNull();
        updated.PrenoteSentAt.Should().BeNull();
        updated.VerifiedAt.Should().BeNull();
    }

    [Fact]
    public async Task PrenoteLifecycle_ApprovedThroughVerified()
    {
        var (db, accounts, batches, _) = CreateHarness();
        var vendorId = await AddVendorAsync(db);
        var created = await accounts.CreateAsync(vendorId, Request(), userId: 1);
        await accounts.ApproveAsync(created.Id, userId: 2);

        // Verify before prenote → blocked.
        var early = () => accounts.MarkVerifiedAsync(created.Id, userId: 1);
        await early.Should().ThrowAsync<InvalidOperationException>();

        var prenote = await batches.CreatePrenoteBatchAsync(new DateOnly(2026, 6, 15), userId: 1);
        prenote.IsPrenote.Should().BeTrue();
        prenote.Items.Should().ContainSingle(i => i.Amount == 0m);

        await batches.GenerateAsync(prenote.Id, userId: 1);
        await batches.ReleaseAsync(prenote.Id, userId: 2);

        (await db.VendorBankAccounts.SingleAsync()).Status.Should().Be(VendorBankAccountStatus.PrenoteSent);

        var verified = await accounts.MarkVerifiedAsync(created.Id, userId: 1);
        verified.Status.Should().Be("Verified");
    }

    // ─────────────────────── Batches ───────────────────────

    [Fact]
    public async Task Eligibility_RequiresPayableAccount_AndExcludesBatched()
    {
        var (db, accounts, batches, _) = CreateHarness();
        var vendorId = await AddVendorAsync(db);
        var payment = await AddAchPaymentAsync(db, vendorId, 500m);

        // No payable account yet → listed but not batchable.
        var eligible = await batches.GetEligiblePaymentsAsync();
        eligible.Should().ContainSingle(e => e.VendorPaymentId == payment.Id && e.BankAccountId == null);

        var act = () => batches.CreateAsync([payment.Id], new DateOnly(2026, 6, 15), userId: 1);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no payable*");

        await VerifiedAccountAsync(accounts, batches, db, vendorId);
        var batch = await batches.CreateAsync([payment.Id], new DateOnly(2026, 6, 15), userId: 1);
        batch.TotalAmount.Should().Be(500m);

        // Now in a live batch → no longer eligible.
        (await batches.GetEligiblePaymentsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Generate_ExposureLimit_Blocks()
    {
        var settings = ConfiguredOrigination();
        settings[BankingSettings.ExposureLimitKey] = "100";
        var (db, accounts, batches, _) = CreateHarness(settings);
        var vendorId = await AddVendorAsync(db);
        await VerifiedAccountAsync(accounts, batches, db, vendorId);
        var payment = await AddAchPaymentAsync(db, vendorId, 500m);

        var batch = await batches.CreateAsync([payment.Id], new DateOnly(2026, 6, 15), userId: 1);
        var act = () => batches.GenerateAsync(batch.Id, userId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exposure limit*");
    }

    [Fact]
    public async Task Generate_MissingOriginationSettings_Blocks()
    {
        // Prenote off so the payable account needs no prenote-batch generation in setup —
        // the FIRST generate attempt is the act under test.
        var settings = ConfiguredOrigination(requirePrenote: false);
        settings[BankingSettings.CompanyIdKey] = null;
        var (db, accounts, batches, _) = CreateHarness(settings);
        var vendorId = await AddVendorAsync(db);
        var created = await accounts.CreateAsync(vendorId, Request(), userId: 1);
        await accounts.ApproveAsync(created.Id, userId: 2);
        var payment = await AddAchPaymentAsync(db, vendorId, 50m);
        var batch = await batches.CreateAsync([payment.Id], new DateOnly(2026, 6, 15), userId: 1);

        var act = () => batches.GenerateAsync(batch.Id, userId: 1);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not configured*");
    }

    [Fact]
    public async Task Release_SoD_BlocksCreator_SecondUserReleases_CreatesSucceededTransmissions()
    {
        var (db, accounts, batches, _) = CreateHarness();
        var vendorId = await AddVendorAsync(db);
        await VerifiedAccountAsync(accounts, batches, db, vendorId);
        var payment = await AddAchPaymentAsync(db, vendorId, 750m);

        var batch = await batches.CreateAsync([payment.Id], new DateOnly(2026, 6, 15), userId: 1);
        await batches.GenerateAsync(batch.Id, userId: 1);

        // Release before generate is impossible (we generated); creator release blocked:
        var self = () => batches.ReleaseAsync(batch.Id, userId: 1);
        await self.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Segregation of duties*");

        var released = await batches.ReleaseAsync(batch.Id, userId: 2);
        released.Status.Should().Be("Released");

        var transmission = await db.PaymentTransmissions.SingleAsync();
        transmission.SourceId.Should().Be(payment.Id);
        transmission.Status.Should().Be(PaymentTransmissionStatus.Succeeded);
        transmission.SubmissionRef.Should().StartWith(released.BatchNumber + "/07100030");
    }

    [Fact]
    public async Task GenerateAndDownload_FileIsValidNacha_AndTracesPersist()
    {
        var (db, accounts, batches, _) = CreateHarness();
        var vendorId = await AddVendorAsync(db);
        await VerifiedAccountAsync(accounts, batches, db, vendorId);
        var payment = await AddAchPaymentAsync(db, vendorId, 750m);
        var batch = await batches.CreateAsync([payment.Id], new DateOnly(2026, 6, 15), userId: 1);

        await batches.GenerateAsync(batch.Id, userId: 1);
        var (fileName, contents) = await batches.GetFileAsync(batch.Id);

        fileName.Should().EndWith(".ach");
        var lines = contents.TrimEnd('\n').Split('\n');
        lines.Should().AllSatisfy(l => l.Length.Should().Be(94));
        // The decrypted account number reaches the file (the one allowed seam) …
        contents.Should().Contain("000123456789");
        // … and the persisted item carries its trace.
        (await db.PaymentBatchItems.SingleAsync(i => i.VendorPaymentId == payment.Id))
            .TraceNumber.Should().Be("071000300000001");
    }

    [Fact]
    public async Task Cancel_ReturnsPaymentsToEligiblePool()
    {
        var (db, accounts, batches, _) = CreateHarness();
        var vendorId = await AddVendorAsync(db);
        await VerifiedAccountAsync(accounts, batches, db, vendorId);
        var payment = await AddAchPaymentAsync(db, vendorId, 300m);
        var batch = await batches.CreateAsync([payment.Id], new DateOnly(2026, 6, 15), userId: 1);

        (await batches.GetEligiblePaymentsAsync()).Should().BeEmpty();
        await batches.CancelAsync(batch.Id, userId: 1);
        (await batches.GetEligiblePaymentsAsync())
            .Should().ContainSingle(e => e.VendorPaymentId == payment.Id);
    }

    [Fact]
    public async Task RequirePrenoteOff_ApprovedAccountIsPayable()
    {
        var (db, accounts, batches, _) = CreateHarness(ConfiguredOrigination(requirePrenote: false));
        var vendorId = await AddVendorAsync(db);
        var created = await accounts.CreateAsync(vendorId, Request(), userId: 1);
        await accounts.ApproveAsync(created.Id, userId: 2);
        var payment = await AddAchPaymentAsync(db, vendorId, 100m);

        var batch = await batches.CreateAsync([payment.Id], new DateOnly(2026, 6, 15), userId: 1);
        batch.EntryCount.Should().Be(1);
    }
}
