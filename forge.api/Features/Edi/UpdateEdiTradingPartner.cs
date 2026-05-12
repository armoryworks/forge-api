using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Edi;

public record UpdateEdiTradingPartnerCommand(int Id, UpdateEdiTradingPartnerRequestModel Model) : IRequest<EdiTradingPartnerResponseModel>;

public class UpdateEdiTradingPartnerHandler(AppDbContext db, IMediator mediator)
    : IRequestHandler<UpdateEdiTradingPartnerCommand, EdiTradingPartnerResponseModel>
{
    public async Task<EdiTradingPartnerResponseModel> Handle(
        UpdateEdiTradingPartnerCommand request, CancellationToken cancellationToken)
    {
        var partner = await db.EdiTradingPartners
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Trading partner {request.Id} not found");

        var model = request.Model;
        if (model.Name is not null) partner.Name = model.Name;
        if (model.CustomerId is not null) partner.CustomerId = model.CustomerId;
        if (model.VendorId is not null) partner.VendorId = model.VendorId;
        if (model.QualifierId is not null) partner.QualifierId = model.QualifierId;
        if (model.QualifierValue is not null) partner.QualifierValue = model.QualifierValue;
        if (model.InterchangeSenderId is not null) partner.InterchangeSenderId = model.InterchangeSenderId;
        if (model.InterchangeReceiverId is not null) partner.InterchangeReceiverId = model.InterchangeReceiverId;
        if (model.ApplicationSenderId is not null) partner.ApplicationSenderId = model.ApplicationSenderId;
        if (model.ApplicationReceiverId is not null) partner.ApplicationReceiverId = model.ApplicationReceiverId;
        if (model.DefaultFormat.HasValue) partner.DefaultFormat = model.DefaultFormat.Value;
        if (model.TransportMethod.HasValue) partner.TransportMethod = model.TransportMethod.Value;
        if (model.TransportConfigJson is not null) partner.TransportConfigJson = model.TransportConfigJson;
        if (model.AutoProcess.HasValue) partner.AutoProcess = model.AutoProcess.Value;
        if (model.RequireAcknowledgment.HasValue) partner.RequireAcknowledgment = model.RequireAcknowledgment.Value;
        if (model.IsActive.HasValue) partner.IsActive = model.IsActive.Value;
        if (model.Notes is not null) partner.Notes = model.Notes;

        await db.SaveChangesAsync(cancellationToken);

        return await mediator.Send(new GetEdiTradingPartnerByIdQuery(partner.Id), cancellationToken);
    }
}
