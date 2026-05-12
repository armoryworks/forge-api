using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Edi;

public record UpdateEdiMappingCommand(int Id, UpdateEdiMappingRequestModel Model) : IRequest<EdiMappingResponseModel>;

public class UpdateEdiMappingHandler(AppDbContext db)
    : IRequestHandler<UpdateEdiMappingCommand, EdiMappingResponseModel>
{
    public async Task<EdiMappingResponseModel> Handle(
        UpdateEdiMappingCommand request, CancellationToken cancellationToken)
    {
        var mapping = await db.EdiMappings
            .FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"EDI mapping {request.Id} not found");

        var model = request.Model;
        if (model.TransactionSet is not null) mapping.TransactionSet = model.TransactionSet;
        if (model.Name is not null) mapping.Name = model.Name;
        if (model.FieldMappingsJson is not null) mapping.FieldMappingsJson = model.FieldMappingsJson;
        if (model.ValueTranslationsJson is not null) mapping.ValueTranslationsJson = model.ValueTranslationsJson;
        if (model.IsDefault.HasValue) mapping.IsDefault = model.IsDefault.Value;
        if (model.Notes is not null) mapping.Notes = model.Notes;

        await db.SaveChangesAsync(cancellationToken);

        return new EdiMappingResponseModel
        {
            Id = mapping.Id,
            TradingPartnerId = mapping.TradingPartnerId,
            TransactionSet = mapping.TransactionSet,
            Name = mapping.Name,
            FieldMappingsJson = mapping.FieldMappingsJson,
            ValueTranslationsJson = mapping.ValueTranslationsJson,
            IsDefault = mapping.IsDefault,
            Notes = mapping.Notes,
        };
    }
}
