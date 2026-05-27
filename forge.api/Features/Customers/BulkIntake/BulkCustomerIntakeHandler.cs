using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Customers.BulkIntake;

/// <summary>
/// C2 — customer bulk-intake. One handler powers preview (dry-run) and commit; the only
/// difference is whether Created rows are inserted + activity-logged. Per row:
///   1. Name required (Invalid otherwise).
///   2. Within-batch dedup by name/email (DuplicateWithinBatch).
///   3. Existing-customer dedup by name OR email (DuplicateExistingCustomer).
///   4. Otherwise Created.
/// Simpler than the leads pipeline (no strategies / suppression / outreach prefs).
/// </summary>
public record BulkCustomerIntakeCommand(BulkCustomerIntakeRequest Request, bool Commit)
    : IRequest<BulkCustomerIntakeResponseModel>;

public class BulkCustomerIntakeHandler(AppDbContext db)
    : IRequestHandler<BulkCustomerIntakeCommand, BulkCustomerIntakeResponseModel>
{
    public async Task<BulkCustomerIntakeResponseModel> Handle(BulkCustomerIntakeCommand request, CancellationToken ct)
    {
        var rows = request.Request.Rows ?? [];
        if (rows.Count == 0)
            return new BulkCustomerIntakeResponseModel(0, 0, 0, []);
        if (rows.Count > 1000)
            throw new InvalidOperationException("Bulk intake is capped at 1000 rows per upload.");

        var names = rows.Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .Select(r => r.Name.Trim().ToLowerInvariant()).Distinct().ToList();
        var emails = rows.Where(r => !string.IsNullOrWhiteSpace(r.Email))
            .Select(r => r.Email!.Trim().ToLowerInvariant()).Distinct().ToList();

        var existingByName = (await db.Customers.AsNoTracking()
                .Where(c => names.Contains(c.Name.ToLower()))
                .Select(c => new { c.Id, c.Name })
                .ToListAsync(ct))
            .GroupBy(c => c.Name.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().Id);

        var existingByEmail = (await db.Customers.AsNoTracking()
                .Where(c => c.Email != null && emails.Contains(c.Email.ToLower()))
                .Select(c => new { c.Id, c.Email })
                .ToListAsync(ct))
            .Where(c => c.Email != null)
            .GroupBy(c => c.Email!.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().Id);

        var results = new List<BulkCustomerIntakeRowResult>(rows.Count);
        var seenNames = new HashSet<string>();
        var seenEmails = new HashSet<string>();
        var toCreate = new List<Customer>();

        foreach (var row in rows)
        {
            var key = row.ExternalRowKey;

            if (string.IsNullOrWhiteSpace(row.Name))
            {
                results.Add(new BulkCustomerIntakeRowResult(key, BulkCustomerIntakeRowStatus.Invalid, null, null, "Name is required"));
                continue;
            }

            var nameNorm = row.Name.Trim().ToLowerInvariant();
            var emailNorm = string.IsNullOrWhiteSpace(row.Email) ? null : row.Email.Trim().ToLowerInvariant();

            if (seenNames.Contains(nameNorm) || (emailNorm is not null && seenEmails.Contains(emailNorm)))
            {
                results.Add(new BulkCustomerIntakeRowResult(key, BulkCustomerIntakeRowStatus.DuplicateWithinBatch, null, null, "Duplicate name/email earlier in batch"));
                continue;
            }

            if (existingByName.TryGetValue(nameNorm, out var matchByName))
            {
                results.Add(new BulkCustomerIntakeRowResult(key, BulkCustomerIntakeRowStatus.DuplicateExistingCustomer, null, matchByName, "Name matches an existing customer"));
                continue;
            }
            if (emailNorm is not null && existingByEmail.TryGetValue(emailNorm, out var matchByEmail))
            {
                results.Add(new BulkCustomerIntakeRowResult(key, BulkCustomerIntakeRowStatus.DuplicateExistingCustomer, null, matchByEmail, "Email matches an existing customer"));
                continue;
            }

            seenNames.Add(nameNorm);
            if (emailNorm is not null) seenEmails.Add(emailNorm);

            toCreate.Add(new Customer
            {
                Name = row.Name.Trim(),
                CompanyName = string.IsNullOrWhiteSpace(row.CompanyName) ? null : row.CompanyName.Trim(),
                Email = string.IsNullOrWhiteSpace(row.Email) ? null : row.Email.Trim(),
                Phone = string.IsNullOrWhiteSpace(row.Phone) ? null : row.Phone.Trim(),
            });
            results.Add(new BulkCustomerIntakeRowResult(key, BulkCustomerIntakeRowStatus.Created, null, null, null));
        }

        if (request.Commit && toCreate.Count > 0)
        {
            db.Customers.AddRange(toCreate);
            await db.SaveChangesAsync(ct);

            using var created = toCreate.GetEnumerator();
            for (var i = 0; i < results.Count; i++)
            {
                if (results[i].Status == BulkCustomerIntakeRowStatus.Created && created.MoveNext())
                    results[i] = results[i] with { CreatedCustomerId = created.Current.Id };
            }

            foreach (var customer in toCreate)
                db.LogActivityAt("bulk-intake-created", $"Created customer via bulk import: {customer.Name}", ("Customer", customer.Id));
            await db.SaveChangesAsync(ct);
        }

        var createdCount = results.Count(r => r.Status == BulkCustomerIntakeRowStatus.Created);
        return new BulkCustomerIntakeResponseModel(rows.Count, createdCount, results.Count - createdCount, results);
    }
}
