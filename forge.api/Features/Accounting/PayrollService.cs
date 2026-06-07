using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <inheritdoc />
public sealed class PayrollService(AppDbContext db, IPostingEngine postingEngine) : IPayrollService
{
    private const string KeyWageExpense = "WAGE_EXPENSE";
    private const string KeyEmployerTaxExpense = "EMPLOYER_PAYROLL_TAX_EXPENSE";
    private const string KeyEmployeeTaxPayable = "EMPLOYEE_TAX_PAYABLE";
    private const string KeyEmployerTaxPayable = "EMPLOYER_TAX_PAYABLE";
    private const string KeyNetPayPayable = "NET_PAY_PAYABLE";

    public async Task<PayRunModel> CreatePayRunAsync(CreatePayRunModel model, CancellationToken ct = default)
    {
        if (model.GrossWages <= 0m)
            throw new InvalidOperationException("Gross wages must be positive.");
        if (model.EmployeeTaxWithheld < 0m || model.EmployerTax < 0m)
            throw new InvalidOperationException("Tax amounts cannot be negative.");
        if (model.EmployeeTaxWithheld > model.GrossWages)
            throw new InvalidOperationException("Employee tax withheld cannot exceed gross wages.");

        var run = new PayRun
        {
            BookId = model.BookId,
            PayDate = model.PayDate,
            PeriodStart = model.PeriodStart,
            PeriodEnd = model.PeriodEnd,
            GrossWages = model.GrossWages,
            EmployeeTaxWithheld = model.EmployeeTaxWithheld,
            EmployerTax = model.EmployerTax,
            Status = PayRunStatus.Draft,
        };
        db.PayRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return Map(run);
    }

    public async Task<IReadOnlyList<PayRunModel>> ListAsync(int bookId, CancellationToken ct = default)
    {
        var runs = await db.PayRuns.AsNoTracking()
            .Where(r => r.BookId == bookId)
            .OrderByDescending(r => r.PayDate).ThenByDescending(r => r.Id)
            .ToListAsync(ct);
        return runs.Select(Map).ToList();
    }

    public async Task<PayRunModel> PostPayRunAsync(int payRunId, int postedByUserId, CancellationToken ct = default)
    {
        var run = await db.PayRuns.FirstOrDefaultAsync(r => r.Id == payRunId, ct)
            ?? throw new KeyNotFoundException($"Pay run {payRunId} not found.");

        if (run.Status == PayRunStatus.Posted)
            throw new InvalidOperationException($"Pay run {payRunId} is already posted.");

        var currencyId = await db.Books.AsNoTracking()
            .Where(b => b.Id == run.BookId).Select(b => (int?)b.FunctionalCurrencyId).FirstOrDefaultAsync(ct)
            ?? throw new PostingException("NO_POSTING_BOOK", $"Book {run.BookId} not found for payroll.");

        var lines = new List<PostingLine>
        {
            new() { AccountKey = KeyWageExpense, Debit = run.GrossWages, Description = "Gross wages" },
            new() { AccountKey = KeyNetPayPayable, Credit = run.NetPay, Description = "Net pay payable" },
        };
        if (run.EmployeeTaxWithheld > 0m)
            lines.Add(new PostingLine { AccountKey = KeyEmployeeTaxPayable, Credit = run.EmployeeTaxWithheld, Description = "Employee tax withheld" });
        if (run.EmployerTax > 0m)
        {
            lines.Add(new PostingLine { AccountKey = KeyEmployerTaxExpense, Debit = run.EmployerTax, Description = "Employer payroll tax expense" });
            lines.Add(new PostingLine { AccountKey = KeyEmployerTaxPayable, Credit = run.EmployerTax, Description = "Employer payroll tax payable" });
        }

        var entry = await postingEngine.PostAsync(new PostingRequest
        {
            BookId = run.BookId,
            EntryDate = run.PayDate,
            Source = JournalSource.Payroll,
            SourceType = "PayRun",
            SourceId = run.Id,
            CurrencyId = currencyId,
            Memo = $"Payroll {run.PeriodStart:yyyy-MM-dd}–{run.PeriodEnd:yyyy-MM-dd}",
            IdempotencyKey = $"{JournalSource.Payroll}:PayRun:{run.Id}",
            Lines = lines,
        }, postedByUserId, ct);

        run.Status = PayRunStatus.Posted;
        run.JournalEntryId = entry.Id;
        await db.SaveChangesAsync(ct);
        return Map(run);
    }

    private static PayRunModel Map(PayRun r) => new(
        r.Id, r.BookId, r.PayDate, r.PeriodStart, r.PeriodEnd, r.GrossWages, r.EmployeeTaxWithheld,
        r.EmployerTax, r.NetPay, r.Status, r.JournalEntryId);
}
