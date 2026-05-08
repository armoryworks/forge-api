using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using QBEngineer.Core.Enums;
using QBEngineer.Core.Models;
using QBEngineer.Data.Context;
using QBEngineer.Data.Extensions;

namespace QBEngineer.Api.Features.Customers;

public record UpdateContactInteractionCommand(
    int CustomerId,
    int InteractionId,
    string Type,
    string Subject,
    string? Body,
    DateTimeOffset InteractionDate,
    int? DurationMinutes) : IRequest<ContactInteractionResponseModel>;

public class UpdateContactInteractionValidator : AbstractValidator<UpdateContactInteractionCommand>
{
    public UpdateContactInteractionValidator()
    {
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Body).MaximumLength(4000);
        RuleFor(x => x.Type).NotEmpty().Must(t => Enum.TryParse<InteractionType>(t, true, out _))
            .WithMessage("Invalid interaction type");
    }
}

public class UpdateContactInteractionHandler(AppDbContext db)
    : IRequestHandler<UpdateContactInteractionCommand, ContactInteractionResponseModel>
{
    public async Task<ContactInteractionResponseModel> Handle(
        UpdateContactInteractionCommand request, CancellationToken cancellationToken)
    {
        var interaction = await db.ContactInteractions
            .Include(ci => ci.Contact)
            .FirstOrDefaultAsync(ci => ci.Id == request.InteractionId
                && ci.Contact.CustomerId == request.CustomerId, cancellationToken)
            ?? throw new KeyNotFoundException($"Interaction {request.InteractionId} not found for customer {request.CustomerId}");

        var changedFields = new List<string>();
        var newType = Enum.Parse<InteractionType>(request.Type, true);
        if (newType != interaction.Type) { interaction.Type = newType; changedFields.Add("type"); }
        if (request.Subject != interaction.Subject) { interaction.Subject = request.Subject; changedFields.Add("subject"); }
        if (request.Body != interaction.Body) { interaction.Body = request.Body; changedFields.Add("body"); }
        if (request.InteractionDate != interaction.InteractionDate)
        {
            interaction.InteractionDate = request.InteractionDate;
            changedFields.Add("interactionDate");
        }
        if (request.DurationMinutes != interaction.DurationMinutes)
        {
            interaction.DurationMinutes = request.DurationMinutes;
            changedFields.Add("durationMinutes");
        }

        if (changedFields.Count > 0)
        {
            db.LogActivityAt(
                "interaction-updated",
                $"Updated interaction ({interaction.Subject}) — {changedFields.Count} field{(changedFields.Count == 1 ? "" : "s")}: {string.Join(", ", changedFields)}",
                ("Customer", request.CustomerId),
                ("Contact", interaction.ContactId));
        }

        await db.SaveChangesAsync(cancellationToken);

        var userInfo = await db.Users
            .Where(u => u.Id == interaction.UserId)
            .Select(u => new { u.FirstName, u.LastName })
            .FirstAsync(cancellationToken);

        return new ContactInteractionResponseModel(
            interaction.Id,
            interaction.ContactId,
            $"{interaction.Contact.LastName}, {interaction.Contact.FirstName}",
            interaction.UserId,
            $"{userInfo.LastName}, {userInfo.FirstName}",
            interaction.Type.ToString(),
            interaction.Subject,
            interaction.Body,
            interaction.InteractionDate,
            interaction.DurationMinutes,
            interaction.CreatedAt);
    }
}
