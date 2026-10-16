using System.Security.Cryptography;

namespace Forge.Api.Features.Admin;

internal static class SetupTokenCode
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    internal static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(8);
        var code = new char[8];
        for (var i = 0; i < 8; i++)
            code[i] = Alphabet[bytes[i] % Alphabet.Length];
        return $"{new string(code, 0, 4)}-{new string(code, 4, 4)}";
    }
}
