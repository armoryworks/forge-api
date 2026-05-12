using MediatR;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Admin;

public record GetAccountingModeQuery : IRequest<AccountingModeResponseModel>;

public record AccountingModeResponseModel(bool IsConfigured, string? ProviderName);

public class GetAccountingModeHandler(
    IAccountingService accountingService,
    ISystemSettingRepository settings) : IRequestHandler<GetAccountingModeQuery, AccountingModeResponseModel>
{
    public async Task<AccountingModeResponseModel> Handle(GetAccountingModeQuery request, CancellationToken cancellationToken)
    {
        var isConnected = await accountingService.TestConnectionAsync(cancellationToken);
        var providerSetting = await settings.FindByKeyAsync("accounting_provider", cancellationToken);

        return new AccountingModeResponseModel(isConnected, providerSetting?.Value);
    }
}
