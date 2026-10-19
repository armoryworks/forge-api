namespace Forge.Api.Features.Banking;

/// <summary>
/// Compares a bank SFTP server's SHA256 host key fingerprint with the pinned
/// banking.sftp.host-key-fingerprint. SSH.NET presents unpadded base64 without the <c>SHA256:</c>
/// prefix; the pin is accepted with or without the prefix and padding.
/// </summary>
public static class SftpHostKeyPin
{
    /// <summary>The fingerprint as bare unpadded base64, or null when blank.</summary>
    public static string? Normalise(string? fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
            return null;

        var value = fingerprint.Trim();
        if (value.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
            value = value["SHA256:".Length..];
        value = value.TrimEnd('=');
        return value.Length == 0 ? null : value;
    }

    /// <summary>True only when a pin is set and equals the presented fingerprint exactly.</summary>
    public static bool Matches(string? pinned, string? presented)
    {
        var expected = Normalise(pinned);
        var actual = Normalise(presented);
        return expected is not null && actual is not null && string.Equals(expected, actual, StringComparison.Ordinal);
    }
}
