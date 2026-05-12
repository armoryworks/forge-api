using Riok.Mapperly.Abstractions;

using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Mappers;

[Mapper]
public static partial class CustomerMapper
{
    [MapperIgnoreSource(nameof(Customer.Contacts))]
    [MapperIgnoreSource(nameof(Customer.Jobs))]
    [MapperIgnoreSource(nameof(Customer.Addresses))]
    [MapperIgnoreSource(nameof(Customer.SalesOrders))]
    [MapperIgnoreSource(nameof(Customer.Quotes))]
    [MapperIgnoreSource(nameof(Customer.Invoices))]
    [MapperIgnoreSource(nameof(Customer.Payments))]
    [MapperIgnoreSource(nameof(Customer.PriceLists))]
    [MapperIgnoreSource(nameof(Customer.RecurringOrders))]
    [MapperIgnoreSource(nameof(Customer.IsActive))]
    [MapperIgnoreSource(nameof(Customer.ExternalId))]
    [MapperIgnoreSource(nameof(Customer.ExternalRef))]
    [MapperIgnoreSource(nameof(Customer.Provider))]
    [MapperIgnoreSource(nameof(Customer.CompanyName))]
    [MapperIgnoreSource(nameof(Customer.Email))]
    [MapperIgnoreSource(nameof(Customer.Phone))]
    [MapperIgnoreSource(nameof(Customer.CreditLimit))]
    [MapperIgnoreSource(nameof(Customer.IsOnCreditHold))]
    [MapperIgnoreSource(nameof(Customer.CreditHoldReason))]
    [MapperIgnoreSource(nameof(Customer.CreditHoldAt))]
    [MapperIgnoreSource(nameof(Customer.CreditHoldById))]
    [MapperIgnoreSource(nameof(Customer.LastCreditReviewDate))]
    [MapperIgnoreSource(nameof(Customer.CreditReviewFrequencyDays))]
    [MapperIgnoreSource(nameof(Customer.IsTaxExempt))]
    [MapperIgnoreSource(nameof(Customer.TaxExemptionId))]
    [MapperIgnoreSource(nameof(Customer.IsActiveForNewTransactions))]
    [MapperIgnoreSource(nameof(Customer.DeactivationDate))]
    [MapperIgnoreSource(nameof(Customer.DefaultTaxCodeId))]
    [MapperIgnoreSource(nameof(Customer.DefaultTaxCode))]
    [MapperIgnoreSource(nameof(Customer.DefaultCurrency))]
    [MapperIgnoreSource(nameof(Customer.SourceLead))]
    [MapperIgnoreSource(nameof(Customer.IsFdaRegulated))]
    [MapperIgnoreSource(nameof(Customer.IsAerospace))]
    [MapperIgnoreSource(nameof(Customer.IsAutomotive))]
    [MapperIgnoreSource(nameof(Customer.IsItarControlled))]
    [MapperIgnoreSource(nameof(Customer.IsReferenceOk))]
    [MapperIgnoreSource(nameof(Customer.ReferenceNotes))]
    [MapperIgnoreSource(nameof(BaseAuditableEntity.CreatedAt))]
    [MapperIgnoreSource(nameof(BaseAuditableEntity.UpdatedAt))]
    [MapperIgnoreSource(nameof(BaseAuditableEntity.DeletedAt))]
    [MapperIgnoreSource(nameof(BaseAuditableEntity.DeletedBy))]
    [MapperIgnoreSource(nameof(BaseAuditableEntity.IsDeleted))]
    public static partial CustomerResponseModel ToResponseModel(this Customer customer);
}
