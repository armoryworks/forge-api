using FluentValidation;
using MediatR;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Edi;

public record CreateEdiTradingPartnerCommand(CreateEdiTradingPartnerRequestModel Model) : IRequest<EdiTradingPartnerResponseModel>;

public class CreateEdiTradingPartnerValidator : AbstractValidator<CreateEdiTradingPartnerCommand>
{
    public CreateEdiTradingPartnerValidator()
    {
        RuleFor(x => x.Model.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Model.QualifierId).NotEmpty().MaximumLength(10);
        RuleFor(x => x.Model.QualifierValue).NotEmpty().MaximumLength(100);
    }
}

public class CreateEdiTradingPartnerHandler(AppDbContext db, IMediator mediator)
    : IRequestHandler<CreateEdiTradingPartnerCommand, EdiTradingPartnerResponseModel>
{
    public async Task<EdiTradingPartnerResponseModel> Handle(
        CreateEdiTradingPartnerCommand request, CancellationToken cancellationToken)
    {
        var model = request.Model;
        var partner = new EdiTradingPartner
        {
            Name = model.Name,
            CustomerId = model.CustomerId,
            VendorId = model.VendorId,
            QualifierId = model.QualifierId,
            QualifierValue = model.QualifierValue,
            InterchangeSenderId = model.InterchangeSenderId,
            InterchangeReceiverId = model.InterchangeReceiverId,
            ApplicationSenderId = model.ApplicationSenderId,
            ApplicationReceiverId = model.ApplicationReceiverId,
            DefaultFormat = model.DefaultFormat,
            TransportMethod = model.TransportMethod,
            TransportConfigJson = model.TransportConfigJson,
            AutoProcess = model.AutoProcess,
            RequireAcknowledgment = model.RequireAcknowledgment,
            Notes = model.Notes,
        };

        db.EdiTradingPartners.Add(partner);
        await db.SaveChangesAsync(cancellationToken);

        return await mediator.Send(new GetEdiTradingPartnerByIdQuery(partner.Id), cancellationToken);
    }
}
