using MediatR;

using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Admin;

public record TestIntegrationConnectionCommand(string Provider) : IRequest<TestIntegrationResultModel>;

public class TestIntegrationConnectionHandler(
    IEmailService emailService,
    IStorageService storageService,
    IAddressValidationService addressValidationService,
    IDocumentSigningService documentSigningService,
    IAiService aiService,
    IEnumerable<IShippingCarrierService> shippingCarriers,
    IAccountingProviderFactory accountingFactory,
    ILogger<TestIntegrationConnectionHandler> logger) : IRequestHandler<TestIntegrationConnectionCommand, TestIntegrationResultModel>
{
    public async Task<TestIntegrationResultModel> Handle(TestIntegrationConnectionCommand request, CancellationToken ct)
    {
        try
        {
            var (success, message) = request.Provider switch
            {
                "smtp" => await TestSmtp(ct),
                "minio" => await TestMinio(ct),
                "usps" => await TestUsps(ct),
                "docuseal" => await TestDocuSeal(ct),
                "ollama" => await TestOllama(ct),
                "ups" or "fedex" or "dhl" => await TestShippingCarrier(request.Provider, ct),
                "xero" or "freshbooks" or "sage" or "netsuite" or "wave" or "zoho" => await TestAccountingProvider(request.Provider, ct),
                _ => (false, $"Unknown integration provider: {request.Provider}")
            };

            logger.LogInformation("[Integration Test] {Provider}: {Success} — {Message}", request.Provider, success, message);
            return new TestIntegrationResultModel(success, message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Integration Test] {Provider} failed with exception", request.Provider);
            return new TestIntegrationResultModel(false, $"Connection test failed: {ex.Message}");
        }
    }

    private async Task<(bool, string)> TestSmtp(CancellationToken ct)
    {
        var success = await emailService.TestConnectionAsync(ct);
        return (success, success ? "SMTP connection successful" : "SMTP connection failed — check host, port, and credentials");
    }

    private async Task<(bool, string)> TestMinio(CancellationToken ct)
    {
        var success = await storageService.TestConnectionAsync(ct);
        return (success, success ? "MinIO connection successful" : "MinIO connection failed — check endpoint and credentials");
    }

    private async Task<(bool, string)> TestUsps(CancellationToken ct)
    {
        var success = await addressValidationService.TestConnectionAsync(ct);
        return (success, success ? "USPS API connection successful" : "USPS connection failed — check Consumer Key and Consumer Secret");
    }

    private async Task<(bool, string)> TestDocuSeal(CancellationToken ct)
    {
        var success = await documentSigningService.IsAvailableAsync(ct);
        return (success, success ? "DocuSeal connection successful" : "DocuSeal connection failed — check Base URL and API Key");
    }

    private async Task<(bool, string)> TestOllama(CancellationToken ct)
    {
        var success = await aiService.IsAvailableAsync(ct);
        return (success, success ? "Ollama AI connection successful" : "Ollama connection failed — check Base URL and ensure the AI container is running");
    }

    private async Task<(bool, string)> TestShippingCarrier(string carrierId, CancellationToken ct)
    {
        var carrier = shippingCarriers.FirstOrDefault(c => c.CarrierId == carrierId);
        if (carrier is null)
            return (false, $"{carrierId} carrier is not registered");
        if (!carrier.IsConfigured)
            return (false, $"{carrier.CarrierName} credentials are not configured — enter Client ID/Secret in the configuration dialog");

        var address = new ShippingAddress("Test", "123 Test St", "New York", "NY", "10001", "US");
        var pkg = new ShippingPackage(1m, 10m, 6m, 4m);
        var request = new ShipmentRequest(address, address, [pkg], null);

        try
        {
            var rates = await carrier.GetRatesAsync(request, ct);
            return rates.Any()
                ? (true, $"{carrier.CarrierName} connection successful — {rates.Count} rate(s) returned")
                : (false, $"{carrier.CarrierName} connected but returned no rates — verify account number and permissions");
        }
        catch (Exception ex)
        {
            return (false, $"{carrier.CarrierName} connection failed: {ex.Message}");
        }
    }

    private async Task<(bool, string)> TestAccountingProvider(string providerId, CancellationToken ct)
    {
        var provider = accountingFactory.GetProvider(providerId);
        if (provider is null)
            return (false, $"{providerId} accounting provider is not registered");

        var success = await provider.TestConnectionAsync(ct);
        return (success, success
            ? $"{provider.ProviderName} connection successful"
            : $"{provider.ProviderName} connection failed — verify credentials and OAuth authorization");
    }
}
