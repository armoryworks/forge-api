using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Edi;

/// <summary>
/// ⚡ EDI BOUNDARY — picks the transport implementation by the partner's configured method.
/// Sftp → the generic SSH.NET channel; everything else (Manual / As2 / Van / Email / Api) →
/// the no-op manual channel until a partner actually demands it. Adding a method later is one
/// case here plus its implementation — partner onboarding stays values-entry.
/// </summary>
public interface IEdiTransportFactory
{
    IEdiTransportService For(EdiTransportMethod method);
}

/// <inheritdoc />
public sealed class EdiTransportFactory(
    SftpEdiTransportService sftp,
    Forge.Integrations.MockEdiTransportService manual) : IEdiTransportFactory
{
    public IEdiTransportService For(EdiTransportMethod method) => method switch
    {
        EdiTransportMethod.Sftp => sftp,
        _ => manual, // manual upload / not-yet-implemented channels: no-op send, empty poll
    };
}
