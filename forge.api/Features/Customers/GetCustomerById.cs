using MediatR;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Customers;

public record GetCustomerByIdQuery(int Id) : IRequest<CustomerDetailResponseModel>;

public class GetCustomerByIdHandler(ICustomerRepository repo)
    : IRequestHandler<GetCustomerByIdQuery, CustomerDetailResponseModel>
{
    public async Task<CustomerDetailResponseModel> Handle(GetCustomerByIdQuery request, CancellationToken cancellationToken)
    {
        var customer = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Customer {request.Id} not found");

        return new CustomerDetailResponseModel(
            customer.Id,
            customer.Name,
            customer.CompanyName,
            customer.Email,
            customer.Phone,
            customer.IsActive,
            customer.IsTaxExempt,
            customer.TaxExemptionId,
            customer.ExternalId,
            customer.ExternalRef,
            customer.Provider,
            customer.CreatedAt,
            customer.UpdatedAt,
            customer.Contacts
                .OrderByDescending(c => c.IsPrimary)
                .ThenBy(c => c.LastName)
                .Select(c => new ContactResponseModel(
                    c.Id, c.FirstName, c.LastName, c.Email, c.Phone, c.Role, c.IsPrimary))
                .ToList(),
            customer.Jobs
                .OrderByDescending(j => j.CreatedAt)
                .Select(j => new CustomerJobSummaryModel(
                    j.Id, j.JobNumber, j.Title,
                    j.CurrentStage?.Name, j.CurrentStage?.Color,
                    j.DueDate))
                .ToList(),
            // Phase 3 F3 — surface full-record fields so a POST-with-everything
            // can be verified via a single GET round-trip.
            CreditLimit: customer.CreditLimit,
            DefaultTaxCodeId: customer.DefaultTaxCodeId,
            DefaultCurrency: customer.DefaultCurrency,
            BillingAddress: PickAddress(customer, AddressType.Billing),
            ShippingAddress: PickAddress(customer, AddressType.Shipping),
            IsFdaRegulated: customer.IsFdaRegulated,
            IsAerospace: customer.IsAerospace,
            IsAutomotive: customer.IsAutomotive,
            IsItarControlled: customer.IsItarControlled,
            IsReferenceOk: customer.IsReferenceOk,
            ReferenceNotes: customer.ReferenceNotes);
    }

    private static AddressOutput? PickAddress(Customer customer, AddressType type)
    {
        // Prefer the IsDefault row matching the requested type; otherwise the
        // first matching row; otherwise an entry typed Both. Returns null if
        // no addresses are configured at all.
        var match = customer.Addresses
            .Where(a => a.AddressType == type)
            .OrderByDescending(a => a.IsDefault)
            .FirstOrDefault()
            ?? customer.Addresses
                .Where(a => a.AddressType == AddressType.Both)
                .OrderByDescending(a => a.IsDefault)
                .FirstOrDefault();
        return match is null ? null : new AddressOutput(
            match.Line1, match.Line2, match.City, match.State, match.PostalCode, match.Country);
    }
}
