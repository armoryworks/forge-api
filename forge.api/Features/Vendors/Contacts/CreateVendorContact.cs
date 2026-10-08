using FluentValidation;
using MediatR;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Vendors.Contacts;

public record CreateVendorContactCommand(
    int VendorId,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? Mobile,
    string? Fax,
    string? Role,
    bool IsPrimary,
    string? Notes,
    bool IsActive = true) : IRequest<VendorContactResponseModel>;

public class CreateVendorContactValidator : AbstractValidator<CreateVendorContactCommand>
{
    public CreateVendorContactValidator()
    {
        RuleFor(x => x.VendorId).GreaterThan(0);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).MaximumLength(200).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.Phone).MaximumLength(50);
        RuleFor(x => x.Mobile).MaximumLength(50);
        RuleFor(x => x.Fax).MaximumLength(50);
        RuleFor(x => x.Role).MaximumLength(50);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class CreateVendorContactHandler(AppDbContext db)
    : IRequestHandler<CreateVendorContactCommand, VendorContactResponseModel>
{
    public async Task<VendorContactResponseModel> Handle(CreateVendorContactCommand request, CancellationToken cancellationToken)
    {
        await VendorContactRules.EnsureVendorExistsAsync(db, request.VendorId, cancellationToken);

        var contact = new VendorContact
        {
            VendorId = request.VendorId,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = NullIfEmpty(request.Email),
            Phone = NullIfEmpty(request.Phone),
            Mobile = NullIfEmpty(request.Mobile),
            Fax = NullIfEmpty(request.Fax),
            Role = NullIfEmpty(request.Role),
            IsPrimary = request.IsPrimary,
            IsActive = request.IsActive,
            Notes = NullIfEmpty(request.Notes),
        };

        List<VendorContact> demoted = contact.IsPrimary
            ? await VendorContactRules.ClearOtherPrimariesAsync(db, request.VendorId, contact.Id, cancellationToken)
            : [];

        db.VendorContacts.Add(contact);
        db.LogActivityAt(
            "contact-added",
            $"Contact added: {VendorContactRules.DisplayName(contact)}{(contact.IsPrimary ? " — primary" : "")}{VendorContactRules.DescribeReplaced(demoted)}",
            ("Vendor", request.VendorId));

        await db.SaveChangesAsync(cancellationToken);

        return VendorContactRules.ToResponseModel(contact);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
