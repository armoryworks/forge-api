using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Vendors.Contacts;

public record UpdateVendorContactCommand(
    int VendorId,
    int ContactId,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? Mobile,
    string? Fax,
    string? Role,
    bool? IsPrimary,
    bool? IsActive,
    string? Notes) : IRequest<VendorContactResponseModel>;

public class UpdateVendorContactValidator : AbstractValidator<UpdateVendorContactCommand>
{
    public UpdateVendorContactValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100).When(x => x.FirstName is not null);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100).When(x => x.LastName is not null);
        RuleFor(x => x.Email).MaximumLength(200).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.Phone).MaximumLength(50).When(x => x.Phone is not null);
        RuleFor(x => x.Mobile).MaximumLength(50).When(x => x.Mobile is not null);
        RuleFor(x => x.Fax).MaximumLength(50).When(x => x.Fax is not null);
        RuleFor(x => x.Role).MaximumLength(50).When(x => x.Role is not null);
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
    }
}

public class UpdateVendorContactHandler(AppDbContext db)
    : IRequestHandler<UpdateVendorContactCommand, VendorContactResponseModel>
{
    public async Task<VendorContactResponseModel> Handle(UpdateVendorContactCommand request, CancellationToken cancellationToken)
    {
        var contact = await db.VendorContacts
            .FirstOrDefaultAsync(c => c.Id == request.ContactId && c.VendorId == request.VendorId, cancellationToken)
            ?? throw new KeyNotFoundException($"Vendor contact {request.ContactId} not found");

        var changedFields = new List<string>();

        if (request.FirstName is not null && request.FirstName.Trim() != contact.FirstName)
        {
            contact.FirstName = request.FirstName.Trim();
            changedFields.Add("firstName");
        }
        if (request.LastName is not null && request.LastName.Trim() != contact.LastName)
        {
            contact.LastName = request.LastName.Trim();
            changedFields.Add("lastName");
        }
        Patch(request.Email, contact.Email, v => contact.Email = v, "email", changedFields);
        Patch(request.Phone, contact.Phone, v => contact.Phone = v, "phone", changedFields);
        Patch(request.Mobile, contact.Mobile, v => contact.Mobile = v, "mobile", changedFields);
        Patch(request.Fax, contact.Fax, v => contact.Fax = v, "fax", changedFields);
        Patch(request.Role, contact.Role, v => contact.Role = v, "role", changedFields);
        Patch(request.Notes, contact.Notes, v => contact.Notes = v, "notes", changedFields);

        if (request.IsActive.HasValue && request.IsActive.Value != contact.IsActive)
        {
            contact.IsActive = request.IsActive.Value;
            changedFields.Add(contact.IsActive ? "activated" : "deactivated");
        }

        List<VendorContact> demoted = [];
        if (request.IsPrimary.HasValue && request.IsPrimary.Value != contact.IsPrimary)
        {
            contact.IsPrimary = request.IsPrimary.Value;
            changedFields.Add(contact.IsPrimary ? "set-primary" : "cleared-primary");
            if (contact.IsPrimary)
                demoted = await VendorContactRules.ClearOtherPrimariesAsync(db, contact.VendorId, contact.Id, cancellationToken);
        }

        if (changedFields.Count > 0)
        {
            db.LogActivityAt(
                "contact-updated",
                $"Contact updated: {VendorContactRules.DisplayName(contact)} — {changedFields.Count} field{(changedFields.Count == 1 ? "" : "s")}: {string.Join(", ", changedFields)}{VendorContactRules.DescribeReplaced(demoted)}",
                ("Vendor", contact.VendorId));
            await db.SaveChangesAsync(cancellationToken);
        }

        return VendorContactRules.ToResponseModel(contact);
    }

    private static void Patch(string? requested, string? current, Action<string?> set, string field, List<string> changedFields)
    {
        if (requested is null)
            return;
        var next = string.IsNullOrWhiteSpace(requested) ? null : requested.Trim();
        if (next == current)
            return;
        set(next);
        changedFields.Add(field);
    }
}
