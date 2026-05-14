
namespace Forge.Core.Models.Communications;

/// <summary>
/// Wave 8 — payload for "Connect a mailbox / phone" action. The connection
/// row starts with <c>IsConnected=false</c>; an OAuth or webhook handshake
/// flips it true once verification completes.
/// </summary>
public record CreateCommunicationSyncConfigRequestModel(
    CommunicationKind Kind,
    string ProviderId,
    string? DisplayLabel,
    string? ExternalAccountId,
    string? ConfigJson);
