using MediatR;

namespace Forge.Api.Features.Communications;

/// <summary>
/// Reports which OAuth-IMAP providers an admin has configured on this install,
/// so the connections page only offers sign-in flows that can actually start.
/// </summary>
public record GetOAuthImapProvidersQuery : IRequest<List<OAuthImapProviderStatus>>;

public sealed record OAuthImapProviderStatus(string Provider, bool IsConfigured);

public class GetOAuthImapProvidersHandler(IImapOAuthService oauth)
    : IRequestHandler<GetOAuthImapProvidersQuery, List<OAuthImapProviderStatus>>
{
    internal static readonly string[] ProviderKeys = ["google", "microsoft"];

    public async Task<List<OAuthImapProviderStatus>> Handle(
        GetOAuthImapProvidersQuery request, CancellationToken cancellationToken)
    {
        var result = new List<OAuthImapProviderStatus>(ProviderKeys.Length);
        foreach (var key in ProviderKeys)
            result.Add(new OAuthImapProviderStatus(key, await oauth.IsProviderConfiguredAsync(key, cancellationToken)));
        return result;
    }
}
