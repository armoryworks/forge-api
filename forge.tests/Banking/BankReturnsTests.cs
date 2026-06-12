using System.Text;

using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Banking;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Banking;

/// <summary>
/// BANK-002 Phase C — NACHA-standard return/NOC ingestion (bank-agnostic by construction).
/// Proves: addenda-99 returns flip the payment's transmission to Failed with the R-code reason;
/// prenote returns disable the bank account; addenda-98 NOCs record the correction without
/// touching the account; unknown traces are skipped; re-applying the same file is idempotent.
/// </summary>
public class BankReturnsTests
{
    /// <summary>Builds a minimal NACHA return file: one entry-detail + one addenda per item.</summary>
    private static string ReturnFile(params (string Trace, string Code, decimal Amount, string? CorrectedData)[] items)
    {
        var sb = new StringBuilder();
        sb.Append(new string(' ', 94)).Append('\n'); // header noise — parser must tolerate it
        foreach (var (trace, code, amount, corrected) in items)
        {
            var cents = ((long)(amount * 100)).ToString("D10");
            var entry = new StringBuilder(94)
                .Append('6').Append("26")                       // returned checking credit
                .Append("061000104")                            // RDFI echo
                .Append("000123456789".PadRight(17))
                .Append(cents)
                .Append("RETURN".PadRight(15))
                .Append("VENDOR".PadRight(22))
                .Append("  ").Append('1')
                .Append("06100010").Append("0000001");
            sb.Append(entry).Append('\n');

            var isNoc = code.StartsWith('C');
            var addenda = new StringBuilder(94)
                .Append('7')
                .Append(isNoc ? "98" : "99")
                .Append(code.PadRight(3)[..3])
                .Append(trace.PadRight(15)[..15])
                .Append(new string(' ', 14))                    // 21..35 (dates/original RDFI region)
                .Append((corrected ?? string.Empty).PadRight(29)[..29]) // 35..64 corrected data
                .Append(new string(' ', 94 - 64));
            sb.Append(addenda.ToString()[..94]).Append('\n');
        }
        sb.Append(new string('9', 94)).Append('\n');            // blocking filler — must be ignored
        return sb.ToString();
    }

    private static (AppDbContext Db, BankReturnsService Service) CreateHarness()
    {
        var db = TestDbContextFactory.Create();
        return (db, new BankReturnsService(db, new Mock<IMediator>().Object));
    }

    /// <summary>A released batch item with its trace + transmission, mirroring a real release.</summary>
    private static async Task<(PaymentBatchItem Item, PaymentTransmission Transmission)> SeedReleasedPaymentAsync(
        AppDbContext db, string trace)
    {
        var vendor = new Vendor { CompanyName = "Pacific Tool Supply", IsActive = true };
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();

        var account = new VendorBankAccount
        {
            VendorId = vendor.Id, Nickname = "Operating",
            RoutingNumberEncrypted = "enc", AccountNumberEncrypted = "enc",
            RoutingNumberMasked = "•0104", AccountNumberMasked = "•6789",
            Status = VendorBankAccountStatus.Verified, ChangedByUserId = 1,
        };
        var payment = new VendorPayment
        {
            PaymentNumber = "VPAY-RET-1", VendorId = vendor.Id,
            Method = PaymentMethod.BankTransfer, Amount = 420.50m,
            PaymentDate = DateTimeOffset.UtcNow,
        };
        var batch = new PaymentBatch
        {
            BatchNumber = "ACH-00009", Status = PaymentBatchStatus.Released,
            EffectiveEntryDate = new DateOnly(2026, 6, 15), CreatedByUserId = 1,
        };
        db.VendorBankAccounts.Add(account);
        db.VendorPayments.Add(payment);
        db.PaymentBatches.Add(batch);
        await db.SaveChangesAsync();

        var item = new PaymentBatchItem
        {
            PaymentBatchId = batch.Id, VendorPaymentId = payment.Id,
            VendorBankAccountId = account.Id, Amount = payment.Amount, TraceNumber = trace,
        };
        var transmission = new PaymentTransmission
        {
            SourceType = "VendorPayment", SourceId = payment.Id,
            Status = PaymentTransmissionStatus.Succeeded, AttemptCount = 1,
            SubmissionRef = $"ACH-00009/{trace}", Amount = payment.Amount, Method = "BankTransfer",
            CreatedByUserId = 2,
        };
        db.PaymentBatchItems.Add(item);
        db.PaymentTransmissions.Add(transmission);
        await db.SaveChangesAsync();
        return (item, transmission);
    }

    private static async Task<PaymentBatchItem> SeedPrenoteItemAsync(AppDbContext db, string trace)
    {
        var vendor = new Vendor { CompanyName = "ColorCoat", IsActive = true };
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();
        var account = new VendorBankAccount
        {
            VendorId = vendor.Id, Nickname = "Main",
            RoutingNumberEncrypted = "enc", AccountNumberEncrypted = "enc",
            RoutingNumberMasked = "•0104", AccountNumberMasked = "•4321",
            Status = VendorBankAccountStatus.PrenoteSent, ChangedByUserId = 1,
        };
        var batch = new PaymentBatch
        {
            BatchNumber = "ACH-00010", Status = PaymentBatchStatus.Released, IsPrenote = true,
            EffectiveEntryDate = new DateOnly(2026, 6, 15), CreatedByUserId = 1,
        };
        db.VendorBankAccounts.Add(account);
        db.PaymentBatches.Add(batch);
        await db.SaveChangesAsync();
        var item = new PaymentBatchItem
        {
            PaymentBatchId = batch.Id, VendorBankAccountId = account.Id, Amount = 0m, TraceNumber = trace,
        };
        db.PaymentBatchItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    [Fact]
    public async Task PaymentReturn_FlipsTransmissionToFailed_WithReason()
    {
        var (db, service) = CreateHarness();
        var (_, transmission) = await SeedReleasedPaymentAsync(db, "071000300000001");

        var result = await service.ApplyAsync(
            ReturnFile(("071000300000001", "R02", 420.50m, null)), actorUserId: 1);

        result.PaymentsReturned.Should().Be(1);
        var updated = await db.PaymentTransmissions.SingleAsync(t => t.Id == transmission.Id);
        updated.Status.Should().Be(PaymentTransmissionStatus.Failed);
        updated.LastError.Should().Contain("R02").And.Contain("Account closed");
    }

    [Fact]
    public async Task PrenoteReturn_DisablesAccount()
    {
        var (db, service) = CreateHarness();
        var item = await SeedPrenoteItemAsync(db, "071000300000002");

        var result = await service.ApplyAsync(
            ReturnFile(("071000300000002", "R04", 0m, null)), actorUserId: 1);

        result.PrenotesRejected.Should().Be(1);
        (await db.VendorBankAccounts.SingleAsync(a => a.Id == item.VendorBankAccountId))
            .Status.Should().Be(VendorBankAccountStatus.Disabled);
    }

    [Fact]
    public async Task Noc_RecordsCorrection_AccountKeepsWorking()
    {
        var (db, service) = CreateHarness();
        var (item, transmission) = await SeedReleasedPaymentAsync(db, "071000300000003");

        var result = await service.ApplyAsync(
            ReturnFile(("071000300000003", "C01", 420.50m, "0099887766")), actorUserId: 1);

        result.Nocs.Should().Be(1);
        result.PaymentsReturned.Should().Be(0);
        // Nothing breaks: transmission stays Succeeded, account stays Verified.
        (await db.PaymentTransmissions.SingleAsync(t => t.Id == transmission.Id))
            .Status.Should().Be(PaymentTransmissionStatus.Succeeded);
        (await db.VendorBankAccounts.SingleAsync(a => a.Id == item.VendorBankAccountId))
            .Status.Should().Be(VendorBankAccountStatus.Verified);
    }

    [Fact]
    public async Task UnknownTrace_SkippedAndCounted()
    {
        var (db, service) = CreateHarness();
        await SeedReleasedPaymentAsync(db, "071000300000004");

        var result = await service.ApplyAsync(
            ReturnFile(("999999999999999", "R03", 10m, null)), actorUserId: 1);

        result.Unmatched.Should().Be(1);
        result.PaymentsReturned.Should().Be(0);
    }

    [Fact]
    public async Task Reapply_SameFile_IsIdempotent()
    {
        var (db, service) = CreateHarness();
        await SeedReleasedPaymentAsync(db, "071000300000005");
        var file = ReturnFile(("071000300000005", "R02", 420.50m, null));

        await service.ApplyAsync(file, actorUserId: 1);
        var second = await service.ApplyAsync(file, actorUserId: 1);

        second.PaymentsReturned.Should().Be(0);
        second.AlreadyApplied.Should().Be(1);
        (await db.PaymentTransmissions.CountAsync(t => t.Status == PaymentTransmissionStatus.Failed))
            .Should().Be(1);
    }

    [Fact]
    public void Parser_ReadsReasonTraceAndAmount()
    {
        var entries = NachaReturnParser.Parse(ReturnFile(
            ("071000300000007", "R02", 123.45m, null),
            ("071000300000008", "C02", 0m, "071000301")));

        entries.Should().HaveCount(2);
        entries[0].Should().BeEquivalentTo(
            new NachaReturnEntry("071000300000007", "R02", false, 123.45m, null));
        entries[1].IsNotificationOfChange.Should().BeTrue();
        entries[1].CorrectedData.Should().Be("071000301");
    }
}
