using FluentValidation;
using MediatR;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Vendors;

public record CreateVendorCommand(
    string CompanyName,
    string? ContactName,
    string? Email,
    string? Phone,
    string? Address,
    string? City,
    string? State,
    string? ZipCode,
    string? Country,
    string? PaymentTerms,
    string? Notes) : IRequest<VendorListItemModel>;

public class CreateVendorValidator : AbstractValidator<CreateVendorCommand>
{
    public CreateVendorValidator()
    {
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContactName).MaximumLength(200);
        RuleFor(x => x.Email).MaximumLength(200).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.Phone).MaximumLength(50);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class CreateVendorHandler(IVendorRepository repo)
    : IRequestHandler<CreateVendorCommand, VendorListItemModel>
{
    public async Task<VendorListItemModel> Handle(CreateVendorCommand request, CancellationToken cancellationToken)
    {
        var vendor = new Vendor
        {
            CompanyName = request.CompanyName,
            ContactName = request.ContactName,
            Email = request.Email,
            Phone = request.Phone,
            Address = request.Address,
            City = request.City,
            State = request.State,
            ZipCode = request.ZipCode,
            Country = request.Country,
            PaymentTerms = request.PaymentTerms,
            Notes = request.Notes,
        };

        await repo.AddAsync(vendor, cancellationToken);
        await repo.SaveChangesAsync(cancellationToken);

        return new VendorListItemModel(
            vendor.Id, vendor.CompanyName, vendor.ContactName,
            vendor.Email, vendor.Phone, vendor.IsActive, 0, vendor.CreatedAt);
    }
}
