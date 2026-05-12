using FluentValidation;
using MediatR;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Edi;

public record CreateEdiMappingCommand(int TradingPartnerId, CreateEdiMappingRequestModel Model) : IRequest<EdiMappingResponseModel>;

public class CreateEdiMappingValidator : AbstractValidator<CreateEdiMappingCommand>
{
    public CreateEdiMappingValidator()
    {
        RuleFor(x => x.Model.TransactionSet).NotEmpty().MaximumLength(10);
        RuleFor(x => x.Model.Name).NotEmpty().MaximumLength(200);
    }
}

public class CreateEdiMappingHandler(AppDbContext db)
    : IRequestHandler<CreateEdiMappingCommand, EdiMappingResponseModel>
{
    public async Task<EdiMappingResponseModel> Handle(
        CreateEdiMappingCommand request, CancellationToken cancellationToken)
    {
        var model = request.Model;
        var mapping = new EdiMapping
        {
            TradingPartnerId = request.TradingPartnerId,
            TransactionSet = model.TransactionSet,
            Name = model.Name,
            FieldMappingsJson = model.FieldMappingsJson,
            ValueTranslationsJson = model.ValueTranslationsJson,
            IsDefault = model.IsDefault,
            Notes = model.Notes,
        };

        db.EdiMappings.Add(mapping);
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
