using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CompanyLocations;

public record UpdateCompanyLocationCommand(
    int Id,
    string Name,
    string Line1,
    string? Line2,
    string City,
    string State,
    string PostalCode,
    string Country,
    string? Phone,
    bool IsActive) : IRequest<CompanyLocationResponseModel>;

public class UpdateCompanyLocationValidator : AbstractValidator<UpdateCompanyLocationCommand>
{
    public UpdateCompanyLocationValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Line1).NotEmpty().MaximumLength(200);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.State).NotEmpty().MaximumLength(50);
        RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Country).NotEmpty().MaximumLength(10);
    }
}

public class UpdateCompanyLocationHandler(AppDbContext db)
    : IRequestHandler<UpdateCompanyLocationCommand, CompanyLocationResponseModel>
{
    public async Task<CompanyLocationResponseModel> Handle(
        UpdateCompanyLocationCommand request, CancellationToken ct)
    {
        var location = await db.CompanyLocations
            .FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Company location {request.Id} not found");

        location.Name = request.Name.Trim();
        location.Line1 = request.Line1.Trim();
        location.Line2 = request.Line2?.Trim();
        location.City = request.City.Trim();
        location.State = request.State.Trim();
        location.PostalCode = request.PostalCode.Trim();
        location.Country = request.Country.Trim();
        location.Phone = request.Phone?.Trim();
        location.IsActive = request.IsActive;

        await db.SaveChangesAsync(ct);

        return new CompanyLocationResponseModel(
            location.Id, location.Name, location.Line1, location.Line2,
            location.City, location.State, location.PostalCode, location.Country,
            location.Phone, location.IsDefault, location.IsActive,
            location.CreatedAt, location.UpdatedAt);
    }
}
