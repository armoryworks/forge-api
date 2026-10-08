using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Workflows;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Customers;

public record UpdateContactCommand(
    int CustomerId,
    int ContactId,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? Role,
    bool? IsPrimary,
    string? Fax = null,
    string? Mobile = null) : IRequest<ContactResponseModel>;

public class UpdateContactValidator : AbstractValidator<UpdateContactCommand>
{
    public UpdateContactValidator()
    {
        RuleFor(x => x.FirstName).MaximumLength(100).When(x => x.FirstName is not null);
        RuleFor(x => x.LastName).MaximumLength(100).When(x => x.LastName is not null);
        RuleFor(x => x.Email).MaximumLength(200).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.Phone).MaximumLength(50).When(x => x.Phone is not null);
        RuleFor(x => x.Mobile).MaximumLength(50).When(x => x.Mobile is not null);
        RuleFor(x => x.Fax).MaximumLength(50).When(x => x.Fax is not null);
        RuleFor(x => x.Role).MaximumLength(50).When(x => x.Role is not null);
    }
}

public class UpdateContactHandler(AppDbContext db)
    : IRequestHandler<UpdateContactCommand, ContactResponseModel>
{
    public async Task<ContactResponseModel> Handle(UpdateContactCommand request, CancellationToken cancellationToken)
    {
        var contact = await db.Contacts
            .FirstOrDefaultAsync(c => c.Id == request.ContactId && c.CustomerId == request.CustomerId, cancellationToken)
            ?? throw new KeyNotFoundException($"Contact {request.ContactId} not found");

        var changedFields = new List<string>();
        if (request.FirstName is not null && request.FirstName != contact.FirstName)
        {
            contact.FirstName = request.FirstName;
            changedFields.Add("firstName");
        }
        if (request.LastName is not null && request.LastName != contact.LastName)
        {
            contact.LastName = request.LastName;
            changedFields.Add("lastName");
        }
        if (request.Email is not null && request.Email.NullIfEmpty() != contact.Email)
        {
            contact.Email = request.Email.NullIfEmpty();
            changedFields.Add("email");
        }
        if (request.Phone is not null && request.Phone.NullIfEmpty() != contact.Phone)
        {
            contact.Phone = request.Phone.NullIfEmpty();
            changedFields.Add("phone");
        }
        if (request.Mobile is not null && request.Mobile.NullIfEmpty() != contact.Mobile)
        {
            contact.Mobile = request.Mobile.NullIfEmpty();
            changedFields.Add("mobile");
        }
        if (request.Fax is not null && request.Fax.NullIfEmpty() != contact.Fax)
        {
            contact.Fax = request.Fax.NullIfEmpty();
            changedFields.Add("fax");
        }
        if (request.Role is not null && request.Role != contact.Role)
        {
            contact.Role = request.Role;
            changedFields.Add("role");
        }
        if (request.IsPrimary.HasValue && request.IsPrimary.Value != contact.IsPrimary)
        {
            contact.IsPrimary = request.IsPrimary.Value;
            changedFields.Add(contact.IsPrimary ? "set-primary" : "cleared-primary");

            if (contact.IsPrimary)
            {
                var demoted = await db.Contacts
                    .Where(c => c.CustomerId == contact.CustomerId && c.Id != contact.Id && c.IsPrimary)
                    .ToListAsync(cancellationToken);
                foreach (var other in demoted)
                {
                    other.IsPrimary = false;
                    db.LogActivityAt(
                        "contact-updated",
                        $"Updated contact ({other.LastName}, {other.FirstName}) — 1 field: cleared-primary",
                        ("Contact", other.Id));
                }
            }
        }

        if (changedFields.Count > 0)
        {
            db.LogActivityAt(
                "contact-updated",
                $"Updated contact ({contact.LastName}, {contact.FirstName}) — {changedFields.Count} field{(changedFields.Count == 1 ? "" : "s")}: {string.Join(", ", changedFields)}",
                ("Customer", request.CustomerId),
                ("Contact", contact.Id));
        }

        await db.SaveChangesAsync(cancellationToken);

        return new ContactResponseModel(
            contact.Id, contact.FirstName, contact.LastName,
            contact.Email, contact.Phone, contact.Role, contact.IsPrimary, contact.Fax, contact.Mobile);
    }
}
