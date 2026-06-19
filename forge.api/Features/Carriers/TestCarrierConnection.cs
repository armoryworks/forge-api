using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Carriers;

/// <summary>
/// Tests a carrier's live API connection from the carrier admin page — resolves the registered carrier
/// service by the carrier's integration service id and does a sample rate-shop. Uses the stored
/// (decrypted) credentials via the adapter's effective-options path, so it tests exactly what shipping
/// will use. Never throws to the caller — connection problems come back as a friendly failure message.
/// </summary>
public record TestCarrierConnectionCommand(int CarrierId) : IRequest<CarrierTestResultModel>;

public class TestCarrierConnectionHandler(
    AppDbContext db,
    IEnumerable<IShippingCarrierService> carriers)
    : IRequestHandler<TestCarrierConnectionCommand, CarrierTestResultModel>
{
    public async Task<CarrierTestResultModel> Handle(TestCarrierConnectionCommand request, CancellationToken ct)
    {
        var carrier = await db.Carriers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CarrierId, ct)
            ?? throw new KeyNotFoundException($"Carrier {request.CarrierId} not found");

        if (carrier.IntegrationKind != CarrierIntegrationKind.Api || string.IsNullOrWhiteSpace(carrier.IntegrationServiceId))
            return new CarrierTestResultModel(false,
                $"{carrier.Name} is a manual carrier — there's no API connection to test.");

        var svc = carriers.FirstOrDefault(c => c.CarrierId == carrier.IntegrationServiceId);
        if (svc is null)
            return new CarrierTestResultModel(false,
                $"{carrier.Name}'s integration isn't running — the API is in mock mode. Restart it in real mode (MockIntegrations=false) to use live carriers.");
        if (!svc.IsConfigured)
            return new CarrierTestResultModel(false,
                $"{carrier.Name} credentials aren't configured — enter the Client ID and Secret first.");

        var address = new ShippingAddress("Test", "123 Test St", "New York", "NY", "10001", "US");
        var probe = new ShipmentRequest(address, address, [new ShippingPackage(1m, 10m, 6m, 4m)], null);
        try
        {
            var rates = await svc.GetRatesAsync(probe, ct);
            return rates.Count > 0
                ? new CarrierTestResultModel(true, $"{carrier.Name} connected — {rates.Count} rate(s) returned.")
                : new CarrierTestResultModel(false,
                    $"{carrier.Name} connected but returned no rates — check the account number and API permissions.");
        }
        catch (Exception ex)
        {
            return new CarrierTestResultModel(false, $"{carrier.Name} connection failed: {ex.Message}");
        }
    }
}
