using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using QBEngineer.Api.Features.Jobs;
using QBEngineer.Core.Entities;
using QBEngineer.Core.Enums;
using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models;
using QBEngineer.Data.Context;
using QBEngineer.Data.Extensions;

namespace QBEngineer.Api.Features.Leads;

public record ConvertLeadCommand(int LeadId, ConvertLeadRequestModel Data) : IRequest<ConvertLeadResponseModel>;

public class ConvertLeadCommandValidator : AbstractValidator<ConvertLeadCommand>
{
    public ConvertLeadCommandValidator()
    {
        RuleFor(x => x.LeadId).GreaterThan(0);

        // Mirror the bounds CreateCustomer validates so a lead-conversion can't
        // produce a customer that direct-create wouldn't have accepted.
        RuleFor(x => x.Data.CreditLimit)
            .InclusiveBetween(0m, 1_000_000_000m)
            .When(x => x.Data.CreditLimit.HasValue)
            .WithMessage("Credit limit must be between 0 and 1,000,000,000.");

        RuleFor(x => x.Data.DefaultCurrency)
            .Matches(@"^[A-Z]{3}$")
            .When(x => !string.IsNullOrEmpty(x.Data.DefaultCurrency))
            .WithMessage("Currency must be a 3-letter ISO 4217 code (e.g. USD).");

        RuleFor(x => x.Data.TaxExemptionId).MaximumLength(50);
        RuleFor(x => x.Data.TaxExemptionId)
            .NotEmpty().When(x => x.Data.IsTaxExempt == true)
            .WithMessage("Tax-exempt customers require an exemption ID on file.");

        // Address blocks: same all-or-nothing rule CreateCustomer enforces.
        // Skipping the block entirely is fine; including it requires the
        // four core fields to be populated.
        When(x => x.Data.BillingAddress is not null, () =>
        {
            RuleFor(x => x.Data.BillingAddress!.Street).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Data.BillingAddress!.City).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Data.BillingAddress!.State).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Data.BillingAddress!.Postal).NotEmpty().MaximumLength(20);
        });
        When(x => x.Data.ShippingAddress is not null, () =>
        {
            RuleFor(x => x.Data.ShippingAddress!.Street).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Data.ShippingAddress!.City).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Data.ShippingAddress!.State).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Data.ShippingAddress!.Postal).NotEmpty().MaximumLength(20);
        });
    }
}

public class ConvertLeadHandler(
    ILeadRepository leadRepo,
    AppDbContext db,
    IMediator mediator) : IRequestHandler<ConvertLeadCommand, ConvertLeadResponseModel>
{
    public async Task<ConvertLeadResponseModel> Handle(ConvertLeadCommand request, CancellationToken cancellationToken)
    {
        var lead = await leadRepo.FindAsync(request.LeadId, cancellationToken)
            ?? throw new KeyNotFoundException($"Lead {request.LeadId} not found");

        if (lead.Status is LeadStatus.Converted)
            throw new InvalidOperationException("Lead has already been converted.");

        if (lead.Status is LeadStatus.Lost)
            throw new InvalidOperationException("Cannot convert a lost lead.");

        var data = request.Data;

        // Create customer from lead — basic identity fields carry over from
        // Lead, the optional richer fields come from the stepper. Provenance
        // is captured via the bidirectional Lead.ConvertedCustomerId /
        // Customer.SourceLead pair set below.
        var customer = new Customer
        {
            Name = lead.CompanyName,
            CompanyName = lead.CompanyName,
            Email = lead.Email,
            Phone = lead.Phone,
            // Wave 2 — richer carry-over from the convert stepper.
            CreditLimit = data.CreditLimit,
            IsTaxExempt = data.IsTaxExempt ?? false,
            TaxExemptionId = data.TaxExemptionId,
            DefaultTaxCodeId = data.DefaultTaxCodeId,
            DefaultCurrency = data.DefaultCurrency,
        };

        // Capture addresses on the customer's Addresses collection so a
        // single SaveChanges persists customer + addresses together. Mirrors
        // the create-customer pattern.
        if (data.BillingAddress is not null)
            customer.Addresses.Add(MapAddress(data.BillingAddress, AddressType.Billing, label: "Billing"));
        if (data.ShippingAddress is not null)
            customer.Addresses.Add(MapAddress(data.ShippingAddress, AddressType.Shipping, label: "Shipping"));

        db.Customers.Add(customer);
        await db.SaveChangesAsync(cancellationToken);

        // Create contact if contact name is available. Defensive name parse:
        // handles "First Last", "First Middle Last" (last word = last name),
        // "Last, First" / "Last, First MI" (comma convention), and single
        // names. The previous Split(' ', 2) silently broke any name with
        // a middle, suffix, or comma form.
        Contact? primaryContact = null;
        if (!string.IsNullOrWhiteSpace(lead.ContactName))
        {
            var (firstName, lastName) = ParseContactName(lead.ContactName);
            primaryContact = new Contact
            {
                CustomerId = customer.Id,
                FirstName = firstName,
                LastName = lastName,
                Email = lead.Email,
                Phone = lead.Phone,
                IsPrimary = true,
            };
            db.Contacts.Add(primaryContact);
        }

        // Update lead — close it out, link to the new customer.
        lead.Status = LeadStatus.Converted;
        lead.ConvertedCustomerId = customer.Id;

        // Indexing-points rule — the conversion is the canonical Lead↔Customer
        // bridge moment, so the activity row appears on BOTH anchors. The
        // lead's activity tab will show "converted to customer #N" and the
        // customer's will show "converted from lead #N — companyName".
        var conversionDescription =
            $"Converted lead → customer: {lead.CompanyName}" +
            (string.IsNullOrEmpty(lead.Source) ? "" : $" (source: {lead.Source})") +
            (string.IsNullOrEmpty(lead.ContactName) ? "" : $" — contact: {lead.ContactName}");
        db.LogActivityAt(
            "lead-converted",
            conversionDescription,
            ("Lead", lead.Id),
            ("Customer", customer.Id));

        await db.SaveChangesAsync(cancellationToken);

        // Optionally create a job
        int? jobId = null;
        if (data.CreateJob)
        {
            // Find default track type
            var defaultTrackType = await db.Set<TrackType>()
                .Where(t => t.IsDefault && t.IsActive)
                .Select(t => t.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (defaultTrackType > 0)
            {
                var jobResult = await mediator.Send(new CreateJobCommand(
                    Title: $"New Job — {lead.CompanyName}",
                    Description: lead.Notes,
                    TrackTypeId: defaultTrackType,
                    AssigneeId: null,
                    CustomerId: customer.Id,
                    Priority: null,
                    DueDate: null), cancellationToken);
                jobId = jobResult.Id;
            }
        }

        return new ConvertLeadResponseModel(customer.Id, jobId);
    }

    private static CustomerAddress MapAddress(AddressInput input, AddressType type, string label) =>
        new()
        {
            Label = label,
            AddressType = type,
            Line1 = input.Street,
            Line2 = input.Line2,
            City = input.City,
            State = input.State,
            PostalCode = input.Postal,
            Country = string.IsNullOrWhiteSpace(input.Country) ? "US" : input.Country!,
            IsDefault = true,
        };

    /// <summary>
    /// Parse a free-form contact name into (firstName, lastName). Defensive
    /// against the common conventions: "First Last", "First Middle Last",
    /// "Last, First", "Last, First MI", and single-word names. Empty input
    /// returns ("", ""). Whitespace-only segments are normalised away.
    /// </summary>
    private static (string firstName, string lastName) ParseContactName(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return ("", "");

        // Comma form: "Last, First [MI]" — common in formal contexts. Take
        // the part before the comma as last, the rest as first.
        var commaIdx = trimmed.IndexOf(',');
        if (commaIdx > 0)
        {
            var lastPart = trimmed[..commaIdx].Trim();
            var firstPart = trimmed[(commaIdx + 1)..].Trim();
            return (firstPart, lastPart);
        }

        // Space form: last word is last name, everything before is first
        // name (covering "First Last", "First Middle Last", honorifics).
        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return (parts[0], "");
        return (string.Join(' ', parts[..^1]), parts[^1]);
    }
}
