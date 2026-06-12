using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Banking;

/// <summary>
/// ⚡ BANKING BOUNDARY — NACHA payment batch lifecycle (BANK-002 Phase A:
/// assemble → generate → download → upload to the bank portal by hand → release).
/// <list type="bullet">
///   <item><b>Eligibility:</b> BankTransfer payments not already in a live batch and whose latest
///         transmission (if any) is terminal-failed/cancelled — the mock-pipeline era and NACHA
///         coexist without double-submitting.</item>
///   <item><b>Exposure limit (§10.1):</b> a batch whose total exceeds banking.exposure-limit cannot
///         generate.</item>
///   <item><b>SoD:</b> release requires a user other than the batch creator. Releasing creates a
///         Succeeded <see cref="PaymentTransmission"/> per member payment (submission accepted —
///         settlement is confirmed by BANK-001 statement reconciliation), which also engages the
///         existing void guard (no voiding a transmitted payment).</item>
///   <item><b>Prenote:</b> a prenote batch carries zero-dollar entries for Approved accounts;
///         release flips them to PrenoteSent.</item>
/// </list>
/// Decrypted account numbers exist ONLY inside <see cref="GenerateAsync"/> — never on a model.
/// </summary>
public interface IPaymentBatchService
{
    Task<IReadOnlyList<PaymentBatchListItemModel>> ListAsync(CancellationToken ct = default);
    Task<PaymentBatchDetailModel> GetDetailAsync(int batchId, CancellationToken ct = default);
    Task<IReadOnlyList<BatchEligiblePaymentModel>> GetEligiblePaymentsAsync(CancellationToken ct = default);
    Task<PaymentBatchDetailModel> CreateAsync(IReadOnlyList<int> vendorPaymentIds, DateOnly effectiveEntryDate, int userId, CancellationToken ct = default);
    Task<PaymentBatchDetailModel> CreatePrenoteBatchAsync(DateOnly effectiveEntryDate, int userId, CancellationToken ct = default);
    Task<PaymentBatchDetailModel> GenerateAsync(int batchId, int userId, CancellationToken ct = default);
    Task<(string FileName, string Contents)> GetFileAsync(int batchId, CancellationToken ct = default);
    Task<PaymentBatchDetailModel> ReleaseAsync(int batchId, int userId, CancellationToken ct = default);
    Task<PaymentBatchDetailModel> CancelAsync(int batchId, int userId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class PaymentBatchService(
    AppDbContext db,
    IVendorBankAccountService bankAccounts,
    IBankingDataProtector protector,
    ISettingsService settings,
    IClock clock) : IPaymentBatchService
{
    public async Task<IReadOnlyList<PaymentBatchListItemModel>> ListAsync(CancellationToken ct = default)
    {
        var batches = await db.PaymentBatches.AsNoTracking()
            .OrderByDescending(b => b.Id)
            .ToListAsync(ct);

        var names = await UserNamesAsync(
            batches.SelectMany(b => new[] { b.CreatedByUserId, b.ReleasedByUserId ?? 0 }).Where(id => id > 0), ct);

        return batches.Select(b => new PaymentBatchListItemModel(
            b.Id, b.BatchNumber, b.Status.ToString(), b.IsPrenote,
            ToOffset(b.EffectiveEntryDate), b.TotalAmount, b.EntryCount,
            b.CreatedByUserId, Name(names, b.CreatedByUserId),
            b.ReleasedByUserId, b.ReleasedByUserId is int r ? Name(names, r) : null,
            b.ReleasedAt, b.CreatedAt)).ToList();
    }

    public async Task<PaymentBatchDetailModel> GetDetailAsync(int batchId, CancellationToken ct = default)
    {
        var batch = await FindAsync(batchId, ct);
        return await ToDetailAsync(batch, ct);
    }

    public async Task<IReadOnlyList<BatchEligiblePaymentModel>> GetEligiblePaymentsAsync(CancellationToken ct = default)
    {
        // ACH = BankTransfer. Wire stays on the per-payment transmission channel.
        var payments = await db.VendorPayments
            .Include(p => p.Vendor)
            .Where(p => p.Method == PaymentMethod.BankTransfer)
            .OrderBy(p => p.Id)
            .ToListAsync(ct);
        if (payments.Count == 0)
            return [];

        var paymentIds = payments.Select(p => p.Id).ToList();

        // Already riding a live (non-cancelled) batch → not eligible.
        var batched = (await db.PaymentBatchItems
            .Where(i => i.VendorPaymentId != null && paymentIds.Contains(i.VendorPaymentId.Value)
                && i.PaymentBatch.Status != PaymentBatchStatus.Cancelled)
            .Select(i => i.VendorPaymentId!.Value)
            .ToListAsync(ct)).ToHashSet();

        // Latest transmission per payment: anything live or already accepted excludes the payment
        // (the mock-era pipeline may own it); Failed/Cancelled payments are re-routable via batch.
        var transmissions = (await db.PaymentTransmissions.AsNoTracking()
            .Where(t => t.SourceType == "VendorPayment" && paymentIds.Contains(t.SourceId))
            .OrderByDescending(t => t.Id)
            .ToListAsync(ct))
            .GroupBy(t => t.SourceId)
            .ToDictionary(g => g.Key, g => g.First().Status);

        var result = new List<BatchEligiblePaymentModel>();
        foreach (var p in payments)
        {
            if (batched.Contains(p.Id))
                continue;
            if (transmissions.TryGetValue(p.Id, out var st)
                && st is not (PaymentTransmissionStatus.Failed or PaymentTransmissionStatus.Cancelled))
                continue;

            var account = await bankAccounts.ResolvePayableAccountAsync(p.VendorId, ct);
            result.Add(new BatchEligiblePaymentModel(
                p.Id, p.PaymentNumber, p.VendorId, p.Vendor.CompanyName, p.Amount, p.PaymentDate,
                account?.Id, account?.Status.ToString(), account?.AccountNumberMasked));
        }
        return result;
    }

    public async Task<PaymentBatchDetailModel> CreateAsync(
        IReadOnlyList<int> vendorPaymentIds, DateOnly effectiveEntryDate, int userId, CancellationToken ct = default)
    {
        if (vendorPaymentIds.Count == 0)
            throw new InvalidOperationException("Select at least one payment for the batch.");

        var eligible = (await GetEligiblePaymentsAsync(ct)).ToDictionary(e => e.VendorPaymentId);

        var batch = new PaymentBatch
        {
            BatchNumber = await NextBatchNumberAsync(ct),
            Status = PaymentBatchStatus.Draft,
            EffectiveEntryDate = effectiveEntryDate,
            CreatedByUserId = userId,
        };

        foreach (var paymentId in vendorPaymentIds.Distinct())
        {
            if (!eligible.TryGetValue(paymentId, out var e))
                throw new InvalidOperationException(
                    $"Payment {paymentId} is not eligible for batching (already batched, transmitted, or not an ACH payment).");
            if (e.BankAccountId is not int accountId)
                throw new InvalidOperationException(
                    $"Payment {e.PaymentNumber}: vendor {e.VendorName} has no payable (verified) bank account.");

            batch.Items.Add(new PaymentBatchItem
            {
                VendorPaymentId = paymentId,
                VendorBankAccountId = accountId,
                Amount = e.Amount,
            });
        }

        batch.TotalAmount = batch.Items.Sum(i => i.Amount);
        batch.EntryCount = batch.Items.Count;

        db.PaymentBatches.Add(batch);
        await db.SaveChangesAsync(ct);

        db.LogActivityAt(
            "batch-created",
            $"Payment batch {batch.BatchNumber} assembled — {batch.EntryCount} payment(s), {batch.TotalAmount:C}",
            ("PaymentBatch", batch.Id));
        await db.SaveChangesAsync(ct);

        return await ToDetailAsync(batch, ct);
    }

    public async Task<PaymentBatchDetailModel> CreatePrenoteBatchAsync(
        DateOnly effectiveEntryDate, int userId, CancellationToken ct = default)
    {
        var accounts = await db.VendorBankAccounts
            .Where(a => a.Status == VendorBankAccountStatus.Approved)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);
        if (accounts.Count == 0)
            throw new InvalidOperationException("No approved bank accounts are awaiting a prenote.");

        var batch = new PaymentBatch
        {
            BatchNumber = await NextBatchNumberAsync(ct),
            Status = PaymentBatchStatus.Draft,
            IsPrenote = true,
            EffectiveEntryDate = effectiveEntryDate,
            CreatedByUserId = userId,
        };
        foreach (var a in accounts)
            batch.Items.Add(new PaymentBatchItem { VendorBankAccountId = a.Id, Amount = 0m });
        batch.EntryCount = batch.Items.Count;

        db.PaymentBatches.Add(batch);
        await db.SaveChangesAsync(ct);

        db.LogActivityAt(
            "batch-created",
            $"Prenote batch {batch.BatchNumber} assembled — {batch.EntryCount} account(s) to verify",
            ("PaymentBatch", batch.Id));
        await db.SaveChangesAsync(ct);

        return await ToDetailAsync(batch, ct);
    }

    public async Task<PaymentBatchDetailModel> GenerateAsync(int batchId, int userId, CancellationToken ct = default)
    {
        var batch = await FindAsync(batchId, ct);
        if (batch.Status != PaymentBatchStatus.Draft)
            throw new InvalidOperationException("Only a draft batch can be generated.");
        if (batch.Items.Count == 0)
            throw new InvalidOperationException("The batch has no entries.");

        // §10.1 exposure control — a batch over the limit can never become a file.
        var exposureLimit = decimal.TryParse(
            await settings.GetStringAsync(BankingSettings.ExposureLimitKey, ct), out var lim) ? lim : 0m;
        if (exposureLimit > 0m && batch.TotalAmount > exposureLimit)
            throw new InvalidOperationException(
                $"Batch total {batch.TotalAmount:C} exceeds the ACH exposure limit {exposureLimit:C}.");

        var origination = await LoadOriginationAsync(ct);

        // The ONLY decryption seam: plaintext numbers live in these locals and the file text.
        var entries = new List<NachaEntry>(batch.Items.Count);
        foreach (var item in batch.Items.OrderBy(i => i.Id))
        {
            var account = item.VendorBankAccount;
            entries.Add(new NachaEntry(
                RoutingNumber: protector.Unprotect(account.RoutingNumberEncrypted)!,
                AccountNumber: protector.Unprotect(account.AccountNumberEncrypted)!,
                IsSavings: account.AccountType == BankAccountType.Savings,
                Amount: item.Amount,
                IndividualId: item.VendorPayment?.PaymentNumber ?? $"PRENOTE-{account.Id}",
                ReceiverName: account.Vendor?.CompanyName ?? $"Vendor {account.VendorId}"));
        }

        batch.FileContents = NachaFileGenerator.Generate(
            origination, entries, batch.EffectiveEntryDate, clock.UtcNow, batch.IsPrenote, batch.Id);

        var traces = NachaFileGenerator.AssignTraceNumbers(origination.OriginatingDfi, entries.Count);
        var ordered = batch.Items.OrderBy(i => i.Id).ToList();
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].TraceNumber = traces[i];

        batch.GeneratedAt = clock.UtcNow;
        batch.Status = PaymentBatchStatus.Generated;

        db.LogActivityAt(
            "batch-generated",
            $"NACHA file generated for batch {batch.BatchNumber} — {batch.EntryCount} entries, {batch.TotalAmount:C}",
            ("PaymentBatch", batch.Id));
        await db.SaveChangesAsync(ct);

        return await ToDetailAsync(batch, ct);
    }

    public async Task<(string FileName, string Contents)> GetFileAsync(int batchId, CancellationToken ct = default)
    {
        var batch = await db.PaymentBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId, ct)
            ?? throw new KeyNotFoundException($"Payment batch {batchId} not found");
        if (batch.FileContents is null)
            throw new InvalidOperationException("The batch has no generated file yet.");
        return ($"{batch.BatchNumber}.ach", batch.FileContents);
    }

    public async Task<PaymentBatchDetailModel> ReleaseAsync(int batchId, int userId, CancellationToken ct = default)
    {
        var batch = await FindAsync(batchId, ct);
        if (batch.Status != PaymentBatchStatus.Generated)
            throw new InvalidOperationException("Only a generated batch can be released (generate the file first).");

        // SoD: the batch creator assembles + uploads; a DIFFERENT user attests the release.
        if (batch.CreatedByUserId == userId)
            throw new InvalidOperationException(
                "Segregation of duties: a batch must be released by a different user than the one who created it.");

        batch.Status = PaymentBatchStatus.Released;
        batch.ReleasedByUserId = userId;
        batch.ReleasedAt = clock.UtcNow;

        if (batch.IsPrenote)
        {
            // Release marks every member account as prenoted; verification comes after the window.
            foreach (var item in batch.Items)
            {
                item.VendorBankAccount.Status = VendorBankAccountStatus.PrenoteSent;
                item.VendorBankAccount.PrenoteSentAt = clock.UtcNow;
            }
        }
        else
        {
            // A Succeeded transmission per payment: submission accepted (NOT settled — BANK-001
            // statement reconciliation confirms settlement). This also engages the existing
            // "no voiding a transmitted payment" guard.
            foreach (var item in batch.Items.Where(i => i.VendorPayment is not null))
            {
                db.PaymentTransmissions.Add(new PaymentTransmission
                {
                    SourceType = "VendorPayment",
                    SourceId = item.VendorPaymentId!.Value,
                    Status = PaymentTransmissionStatus.Succeeded,
                    AttemptCount = 1,
                    LastAttemptAt = clock.UtcNow,
                    SubmissionRef = $"{batch.BatchNumber}/{item.TraceNumber}",
                    Amount = item.Amount,
                    Method = PaymentMethod.BankTransfer.ToString(),
                    CreatedByUserId = userId,
                });
                db.LogActivityAt(
                    "transmission-succeeded",
                    $"Payment {item.VendorPayment!.PaymentNumber} transmitted in batch {batch.BatchNumber} (trace {item.TraceNumber})",
                    ("VendorPayment", item.VendorPaymentId.Value));
            }
        }

        db.LogActivityAt(
            "batch-released",
            $"Batch {batch.BatchNumber} released after portal upload — {batch.EntryCount} entries, {batch.TotalAmount:C}",
            ("PaymentBatch", batch.Id));
        await db.SaveChangesAsync(ct);

        return await ToDetailAsync(batch, ct);
    }

    public async Task<PaymentBatchDetailModel> CancelAsync(int batchId, int userId, CancellationToken ct = default)
    {
        var batch = await FindAsync(batchId, ct);
        if (batch.Status is not (PaymentBatchStatus.Draft or PaymentBatchStatus.Generated))
            throw new InvalidOperationException("Only a draft or generated batch can be cancelled.");

        batch.Status = PaymentBatchStatus.Cancelled;

        db.LogActivityAt(
            "batch-cancelled",
            $"Batch {batch.BatchNumber} cancelled — its payments return to the eligible pool",
            ("PaymentBatch", batch.Id));
        await db.SaveChangesAsync(ct);

        return await ToDetailAsync(batch, ct);
    }

    private async Task<PaymentBatch> FindAsync(int batchId, CancellationToken ct)
        => await db.PaymentBatches
            .Include(b => b.Items).ThenInclude(i => i.VendorPayment)
            .Include(b => b.Items).ThenInclude(i => i.VendorBankAccount).ThenInclude(a => a.Vendor)
            .FirstOrDefaultAsync(b => b.Id == batchId, ct)
            ?? throw new KeyNotFoundException($"Payment batch {batchId} not found");

    private async Task<NachaOrigination> LoadOriginationAsync(CancellationToken ct)
    {
        var origination = new NachaOrigination(
            ImmediateDestination: await Required(BankingSettings.ImmediateDestinationKey, ct),
            ImmediateDestinationName: await Required(BankingSettings.ImmediateDestinationNameKey, ct),
            ImmediateOrigin: await Required(BankingSettings.ImmediateOriginKey, ct),
            ImmediateOriginName: await Required(BankingSettings.ImmediateOriginNameKey, ct),
            CompanyName: await Required(BankingSettings.CompanyNameKey, ct),
            CompanyId: await Required(BankingSettings.CompanyIdKey, ct),
            OriginatingDfi: await Required(BankingSettings.OriginatingDfiKey, ct),
            EntryClassCode: (await settings.GetStringAsync(BankingSettings.EntryClassCodeKey, ct)) ?? "CCD");

        if (!NachaFileGenerator.IsValidRoutingNumber(origination.ImmediateDestination))
            throw new InvalidOperationException(
                "Banking settings: immediate destination is not a valid routing number (ABA checksum).");

        return origination;
    }

    private async Task<string> Required(string key, CancellationToken ct)
        => await settings.GetStringAsync(key, ct) is { Length: > 0 } v
            ? v
            : throw new InvalidOperationException(
                $"Banking setting '{key}' is not configured — complete the NACHA origination settings "
                + "(from the bank's ACH agreement) before generating files.");

    private async Task<string> NextBatchNumberAsync(CancellationToken ct)
    {
        var last = await db.PaymentBatches.IgnoreQueryFilters()
            .OrderByDescending(b => b.Id)
            .Select(b => b.BatchNumber)
            .FirstOrDefaultAsync(ct);
        if (last != null && last.StartsWith("ACH-") && int.TryParse(last[4..], out var n))
            return $"ACH-{n + 1:D5}";
        return "ACH-00001";
    }

    private async Task<PaymentBatchDetailModel> ToDetailAsync(PaymentBatch batch, CancellationToken ct)
    {
        // Ensure items + navs are loaded (a freshly created batch already has them tracked).
        if (batch.Items.Count > 0 && batch.Items.First().VendorBankAccount is null)
            batch = await FindAsync(batch.Id, ct);

        var names = await UserNamesAsync(
            new[] { batch.CreatedByUserId, batch.ReleasedByUserId ?? 0 }.Where(id => id > 0), ct);

        return new PaymentBatchDetailModel(
            batch.Id, batch.BatchNumber, batch.Status.ToString(), batch.IsPrenote,
            ToOffset(batch.EffectiveEntryDate), batch.TotalAmount, batch.EntryCount,
            batch.CreatedByUserId, Name(names, batch.CreatedByUserId),
            batch.ReleasedByUserId, batch.ReleasedByUserId is int r ? Name(names, r) : null,
            batch.ReleasedAt, batch.GeneratedAt, batch.FileContents is not null, batch.CreatedAt,
            batch.Items.OrderBy(i => i.Id).Select(i => new PaymentBatchItemModel(
                i.Id, i.VendorPaymentId, i.VendorPayment?.PaymentNumber,
                i.VendorBankAccount.VendorId,
                i.VendorBankAccount.Vendor?.CompanyName ?? $"Vendor {i.VendorBankAccount.VendorId}",
                i.VendorBankAccount.AccountNumberMasked, i.Amount, i.TraceNumber)).ToList());
    }

    private async Task<Dictionary<int, string>> UserNamesAsync(IEnumerable<int> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        return await db.Users
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", ct);
    }

    private static string Name(Dictionary<int, string> names, int id)
        => names.TryGetValue(id, out var n) ? n : $"User {id}";

    private static DateTimeOffset ToOffset(DateOnly date)
        => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
