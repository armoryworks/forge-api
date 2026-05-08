using FluentValidation;
using MediatR;
using QBEngineer.Core.Entities;
using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models;
using QBEngineer.Data.Context;
using QBEngineer.Data.Extensions;

namespace QBEngineer.Api.Features.Customers;

public record CreateContactCommand(
    int CustomerId,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? Role,
    bool IsPrimary) : IRequest<ContactResponseModel>;

public class CreateContactValidator : AbstractValidator<CreateContactCommand>
{
    public CreateContactValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).MaximumLength(200).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.Phone).MaximumLength(50);
        RuleFor(x => x.Role).MaximumLength(50);
    }
}

public class CreateContactHandler(ICustomerRepository repo, AppDbContext db)
    : IRequestHandler<CreateContactCommand, ContactResponseModel>
{
    public async Task<ContactResponseModel> Handle(CreateContactCommand request, CancellationToken cancellationToken)
    {
        var customer = await repo.FindAsync(request.CustomerId, cancellationToken)
            ?? throw new KeyNotFoundException($"Customer {request.CustomerId} not found");

        var contact = new Contact
        {
            CustomerId = customer.Id,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            Phone = request.Phone,
            Role = request.Role,
            IsPrimary = request.IsPrimary,
        };

        customer.Contacts.Add(contact);
        await repo.SaveChangesAsync(cancellationToken);

        // Indexing-points rule — Contact bridges Customer ↔ a person, so the
        // create event is logged on both anchors. Future cross-customer
        // contact moves will pivot here.
        db.LogActivityAt(
            "contact-added",
            $"Added contact: {contact.LastName}, {contact.FirstName}{(string.IsNullOrEmpty(contact.Role) ? "" : $" ({contact.Role})")}{(contact.IsPrimary ? " — primary" : "")}",
            ("Customer", customer.Id),
            ("Contact", contact.Id));
        await db.SaveChangesAsync(cancellationToken);

        return new ContactResponseModel(
            contact.Id, contact.FirstName, contact.LastName,
            contact.Email, contact.Phone, contact.Role, contact.IsPrimary);
    }
}
