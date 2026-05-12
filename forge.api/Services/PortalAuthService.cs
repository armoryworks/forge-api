using System.Security.Cryptography;
using System.Text;

using Forge.Core.Interfaces;

namespace Forge.Api.Services;

public class PortalAuthService : IPortalAuthService
{
    public (string PlaintextToken, string Hash) GenerateMagicLinkToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (token, HashToken(token));
    }

    public string HashToken(string plaintextToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintextToken));
        return Convert.ToHexString(bytes);
    }
}
