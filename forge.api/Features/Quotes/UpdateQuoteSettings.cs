using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Quotes;

public record UpdateQuoteSettingsCommand(bool? AutoCustomerPoEnabled) : IRequest<QuoteSettingsResponseModel>;

/// <summary>
/// S4a — persists sales-side quote/order settings. Mirrors
/// Features/AutoPo/UpdateAutoPoSettings.cs: nullable fields are patch
/// semantics (only non-null values are upserted), then the fresh state is
/// returned via <see cref="GetQuoteSettingsQuery"/>.
/// </summary>
public class UpdateQuoteSettingsHandler(
    ISystemSettingRepository settings,
    IMediator mediator) : IRequestHandler<UpdateQuoteSettingsCommand, QuoteSettingsResponseModel>
{
    public async Task<QuoteSettingsResponseModel> Handle(UpdateQuoteSettingsCommand request, CancellationToken ct)
    {
        if (request.AutoCustomerPoEnabled.HasValue)
            await settings.UpsertAsync(
                "sales:auto_customer_po_enabled",
                request.AutoCustomerPoEnabled.Value.ToString(),
                "Auto-generate the internal customer-PO document when a quote converts to an order",
                ct);

        await settings.SaveChangesAsync(ct);

        return await mediator.Send(new GetQuoteSettingsQuery(), ct);
    }
}
