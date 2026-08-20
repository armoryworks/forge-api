using MediatR;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Settings;

/// <summary>
/// Per-entity "allow manual numbers" flags (the <c>{entity}.allow_manual_numbers</c> system
/// settings), surfaced together so any create/edit screen can decide whether to offer an editable
/// business-number field. Read-only booleans, hence bootstrap-exempt rather than Admin-gated.
/// </summary>
public record ManualNumberSettingsResponseModel(
    bool Parts,
    bool Customers,
    bool Vendors,
    bool Leads,
    bool SalesOrders,
    bool Quotes,
    bool PurchaseOrders,
    bool Shipments,
    bool Jobs,
    bool Invoices,
    bool Payments);

public record GetManualNumberSettingsQuery() : IRequest<ManualNumberSettingsResponseModel>;

public class GetManualNumberSettingsHandler(ISystemSettingRepository systemSettings)
    : IRequestHandler<GetManualNumberSettingsQuery, ManualNumberSettingsResponseModel>
{
    public async Task<ManualNumberSettingsResponseModel> Handle(GetManualNumberSettingsQuery request, CancellationToken cancellationToken)
    {
        var all = await systemSettings.GetAllAsync(cancellationToken);
        var byKey = all.ToDictionary(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase);

        bool Enabled(string entity) =>
            byKey.TryGetValue($"{entity}.allow_manual_numbers", out var v) && bool.TryParse(v, out var b) && b;

        return new ManualNumberSettingsResponseModel(
            Parts: Enabled("parts"),
            Customers: Enabled("customers"),
            Vendors: Enabled("vendors"),
            Leads: Enabled("leads"),
            SalesOrders: Enabled("sales_orders"),
            Quotes: Enabled("quotes"),
            PurchaseOrders: Enabled("purchase_orders"),
            Shipments: Enabled("shipments"),
            Jobs: Enabled("jobs"),
            Invoices: Enabled("invoices"),
            Payments: Enabled("payments"));
    }
}
