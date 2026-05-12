using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CompanyLocations;

public record CreateCompanyLocationCommand(
    string Name,
    string Line1,
    string? Line2,
    string City,
    string State,
    string PostalCode,
    string Country,
    string? Phone,
    bool IsActive) : IRequest<CompanyLocationResponseModel>;

public class CreateCompanyLocationValidator : AbstractValidator<CreateCompanyLocationCommand>
{
    public CreateCompanyLocationValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Line1).NotEmpty().MaximumLength(200);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.State).NotEmpty().MaximumLength(50);
        RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Country).NotEmpty().MaximumLength(10);
    }
}

public class CreateCompanyLocationHandler(AppDbContext db)
    : IRequestHandler<CreateCompanyLocationCommand, CompanyLocationResponseModel>
{
    public async Task<CompanyLocationResponseModel> Handle(
        CreateCompanyLocationCommand request, CancellationToken ct)
    {
        var hasAny = await db.CompanyLocations.AnyAsync(ct);

        var location = new CompanyLocation
        {
            Name = request.Name.Trim(),
            Line1 = request.Line1.Trim(),
            Line2 = request.Line2?.Trim(),
            City = request.City.Trim(),
            State = request.State.Trim(),
            PostalCode = request.PostalCode.Trim(),
            Country = request.Country.Trim(),
            Phone = request.Phone?.Trim(),
            IsDefault = !hasAny, // First location is always default
            IsActive = request.IsActive,
        };

        db.CompanyLocations.Add(location);
        await db.SaveChangesAsync(ct);

        return new CompanyLocationResponseModel(
            location.Id, location.Name, location.Line1, location.Line2,
            location.City, location.State, location.PostalCode, location.Country,
            location.Phone, location.IsDefault, location.IsActive,
            location.CreatedAt, location.UpdatedAt);
    }
}
