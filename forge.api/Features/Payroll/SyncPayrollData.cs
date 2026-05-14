using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Payroll;

public sealed record SyncPayrollDataCommand : IRequest<int>;

public sealed class SyncPayrollDataHandler(
    AppDbContext db,
    IAccountingProviderFactory providerFactory,
    ILogger<SyncPayrollDataHandler> logger)
    : IRequestHandler<SyncPayrollDataCommand, int>
{
    public async Task<int> Handle(SyncPayrollDataCommand request, CancellationToken ct)
    {
        var provider = await providerFactory.GetActiveProviderAsync(ct);
        if (provider is null)
        {
            logger.LogWarning("No accounting provider configured — skipping payroll sync");
            return 0;
        }

        var usersWithAccounting = await db.Users
            .AsNoTracking()
            .Where(u => u.AccountingEmployeeId != null && u.IsActive)
            .Select(u => new { u.Id, u.AccountingEmployeeId })
            .ToListAsync(ct);

        var totalCreated = 0;

        foreach (var user in usersWithAccounting)
        {
            try
            {
                // Sync pay stubs
                var stubs = await provider.GetPayStubsAsync(user.AccountingEmployeeId!, null, null, ct);
                var existingStubExternalIds = await db.PayStubs
                    .AsNoTracking()
                    .Where(p => p.UserId == user.Id && p.ExternalId != null)
                    .Select(p => p.ExternalId!)
                    .ToListAsync(ct);

                foreach (var stub in stubs.Where(s => !existingStubExternalIds.Contains(s.ExternalId)))
                {
                    var entity = new PayStub
                    {
                        UserId = user.Id,
                        PayPeriodStart = stub.PayPeriodStart,
                        PayPeriodEnd = stub.PayPeriodEnd,
                        PayDate = stub.PayDate,
                        GrossPay = stub.GrossPay,
                        NetPay = stub.NetPay,
                        TotalDeductions = stub.Deductions.Sum(d => d.Amount),
                        TotalTaxes = stub.Deductions
                            .Where(d => d.Category is "FederalTax" or "StateTax" or "SocialSecurity" or "Medicare")
                            .Sum(d => d.Amount),
                        Source = PayrollDocumentSource.Accounting,
                        ExternalId = stub.ExternalId,
                    };

                    foreach (var ded in stub.Deductions)
                    {
                        entity.Deductions.Add(new PayStubDeduction
                        {
                            Category = Enum.TryParse<PayStubDeductionCategory>(ded.Category, out var cat)
                                ? cat
                                : PayStubDeductionCategory.Other,
                            Description = ded.Description,
                            Amount = ded.Amount,
                        });
                    }

                    db.PayStubs.Add(entity);
                    totalCreated++;
                }

                // Sync tax documents
                var taxDocs = await provider.GetTaxDocumentsAsync(user.AccountingEmployeeId!, null, ct);
                var existingTaxExternalIds = await db.TaxDocuments
                    .AsNoTracking()
                    .Where(d => d.UserId == user.Id && d.ExternalId != null)
                    .Select(d => d.ExternalId!)
                    .ToListAsync(ct);

                foreach (var doc in taxDocs.Where(d => !existingTaxExternalIds.Contains(d.ExternalId)))
                {
                    var entity = new TaxDocument
                    {
                        UserId = user.Id,
                        DocumentType = Enum.TryParse<TaxDocumentType>(doc.DocumentType, out var docType)
                            ? docType
                            : TaxDocumentType.Other,
                        TaxYear = doc.TaxYear,
                        EmployerName = doc.EmployerName,
                        Source = PayrollDocumentSource.Accounting,
                        ExternalId = doc.ExternalId,
                    };

                    db.TaxDocuments.Add(entity);
                    totalCreated++;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to sync payroll data for user {UserId}", user.Id);
            }
        }

        if (totalCreated > 0)
            await db.SaveChangesAsync(ct);

        logger.LogInformation("Payroll sync complete — {Count} new records created for {UserCount} users",
            totalCreated, usersWithAccounting.Count);

        return totalCreated;
    }
}
