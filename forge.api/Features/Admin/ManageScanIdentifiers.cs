using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

// --- Get Identifiers ---

public record GetUserScanIdentifiersQuery(int UserId) : IRequest<List<ScanIdentifierResponseModel>>;

public class GetUserScanIdentifiersHandler(AppDbContext db)
    : IRequestHandler<GetUserScanIdentifiersQuery, List<ScanIdentifierResponseModel>>
{
    public async Task<List<ScanIdentifierResponseModel>> Handle(GetUserScanIdentifiersQuery request, CancellationToken cancellationToken)
    {
        return await db.UserScanIdentifiers
            .Where(x => x.UserId == request.UserId)
            .Select(x => new ScanIdentifierResponseModel
            {
                Id = x.Id,
                IdentifierType = x.IdentifierType,
                IdentifierValue = x.IdentifierValue,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt,
            })
            .ToListAsync(cancellationToken);
    }
}

// --- Add Identifier ---

public record AddScanIdentifierCommand(int UserId, string IdentifierType, string IdentifierValue) : IRequest<ScanIdentifierResponseModel>;

public class AddScanIdentifierValidator : AbstractValidator<AddScanIdentifierCommand>
{
    public AddScanIdentifierValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0);
        RuleFor(x => x.IdentifierType).NotEmpty().Must(t => t is "nfc" or "rfid" or "barcode")
            .WithMessage("Type must be 'nfc', 'rfid', or 'barcode'");
        RuleFor(x => x.IdentifierValue).NotEmpty().MaximumLength(200);
    }
}

public class AddScanIdentifierHandler(AppDbContext db) : IRequestHandler<AddScanIdentifierCommand, ScanIdentifierResponseModel>
{
    public async Task<ScanIdentifierResponseModel> Handle(AddScanIdentifierCommand request, CancellationToken cancellationToken)
    {
        var exists = await db.UserScanIdentifiers
            .AnyAsync(x => x.IdentifierType == request.IdentifierType
                && x.IdentifierValue == request.IdentifierValue, cancellationToken);

        if (exists)
            throw new InvalidOperationException("This scan identifier is already registered");

        var identifier = new UserScanIdentifier
        {
            UserId = request.UserId,
            IdentifierType = request.IdentifierType,
            IdentifierValue = request.IdentifierValue,
            IsActive = true,
        };

        db.UserScanIdentifiers.Add(identifier);
        await db.SaveChangesAsync(cancellationToken);

        return new ScanIdentifierResponseModel
        {
            Id = identifier.Id,
            IdentifierType = identifier.IdentifierType,
            IdentifierValue = identifier.IdentifierValue,
            IsActive = identifier.IsActive,
            CreatedAt = identifier.CreatedAt,
        };
    }
}

// --- Remove Identifier ---

public record RemoveScanIdentifierCommand(int Id) : IRequest;

public class RemoveScanIdentifierHandler(AppDbContext db) : IRequestHandler<RemoveScanIdentifierCommand>
{
    public async Task Handle(RemoveScanIdentifierCommand request, CancellationToken cancellationToken)
    {
        var identifier = await db.UserScanIdentifiers
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Scan identifier not found");

        identifier.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
