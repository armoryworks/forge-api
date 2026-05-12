using MediatR;
using Microsoft.Extensions.Options;

using Forge.Core.Models;

namespace Forge.Api.Features.Auth;

public record SsoProviderInfo(string Id, string Name, bool Enabled);

public record GetSsoProvidersQuery : IRequest<List<SsoProviderInfo>>;

public class GetSsoProvidersHandler(
    IOptions<SsoOptions> ssoOptions) : IRequestHandler<GetSsoProvidersQuery, List<SsoProviderInfo>>
{
    public Task<List<SsoProviderInfo>> Handle(GetSsoProvidersQuery request, CancellationToken cancellationToken)
    {
        var opts = ssoOptions.Value;
        var providers = new List<SsoProviderInfo>();

        if (opts.Google.Enabled)
            providers.Add(new SsoProviderInfo("google", "Google", true));
        if (opts.Microsoft.Enabled)
            providers.Add(new SsoProviderInfo("microsoft", "Microsoft", true));
        if (opts.Oidc.Enabled)
            providers.Add(new SsoProviderInfo("oidc", opts.Oidc.DisplayName ?? "SSO", true));

        return Task.FromResult(providers);
    }
}
