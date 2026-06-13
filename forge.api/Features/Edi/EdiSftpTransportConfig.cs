using System.Text.Json;

namespace Forge.Api.Features.Edi;

/// <summary>
/// ⚡ EDI BOUNDARY — typed shape of a partner's SFTP transport configuration. The admin UI edits
/// LABELED FIELDS (never JSON); this record is only the storage serialization for the partner
/// row's polymorphic transport column, with the password held as Data-Protection ciphertext
/// (<c>Forge.EdiTransport</c> purpose). <see cref="OutboundDir"/> is where WE PUT documents for
/// the partner; <see cref="InboundDir"/> is where we poll for theirs.
/// </summary>
public sealed record EdiSftpTransportConfig(
    string Host,
    int Port,
    string Username,
    string PasswordEncrypted,
    string OutboundDir,
    string InboundDir)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static EdiSftpTransportConfig? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<EdiSftpTransportConfig>(json, Options);
        }
        catch (JsonException)
        {
            return null; // legacy/foreign payloads in the column read as unconfigured
        }
    }
}
