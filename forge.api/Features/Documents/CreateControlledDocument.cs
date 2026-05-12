using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Documents;

public record CreateControlledDocumentCommand(
    string Title,
    string? Description,
    string Category,
    int ReviewIntervalDays) : IRequest<ControlledDocumentResponseModel>;

public class CreateControlledDocumentCommandValidator : AbstractValidator<CreateControlledDocumentCommand>
{
    private static readonly string[] ValidCategories = ["SOP", "WI", "Form", "Spec"];

    public CreateControlledDocumentCommandValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(300).WithMessage("Title must not exceed 300 characters.");

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("Category is required.")
            .Must(c => ValidCategories.Contains(c))
            .WithMessage("Category must be one of: SOP, WI, Form, Spec.");

        RuleFor(x => x.ReviewIntervalDays)
            .GreaterThan(0).WithMessage("Review interval must be greater than zero.");
    }
}

public class CreateControlledDocumentHandler(
    AppDbContext db,
    IHttpContextAccessor httpContextAccessor) : IRequestHandler<CreateControlledDocumentCommand, ControlledDocumentResponseModel>
{
    public async Task<ControlledDocumentResponseModel> Handle(CreateControlledDocumentCommand request, CancellationToken cancellationToken)
    {
        var userId = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User not authenticated.");

        var userIdInt = int.Parse(userId);

        var count = await db.ControlledDocuments.CountAsync(cancellationToken);
        var documentNumber = $"DOC-{(count + 1):D5}";

        var document = new ControlledDocument
        {
            DocumentNumber = documentNumber,
            Title = request.Title,
            Description = request.Description,
            Category = request.Category,
            ReviewIntervalDays = request.ReviewIntervalDays,
            Status = ControlledDocumentStatus.Draft,
            OwnerId = userIdInt,
        };

        db.ControlledDocuments.Add(document);
        await db.SaveChangesAsync(cancellationToken);

        return new ControlledDocumentResponseModel(
            document.Id,
            document.DocumentNumber,
            document.Title,
            document.Description,
            document.Category,
            document.CurrentRevision,
            document.Status,
            document.OwnerId,
            document.CheckedOutById,
            document.CheckedOutAt,
            document.ReleasedAt,
            document.ReviewDueDate,
            document.ReviewIntervalDays,
            0,
            document.CreatedAt,
            document.UpdatedAt);
    }
}
