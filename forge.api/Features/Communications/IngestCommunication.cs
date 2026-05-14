using FluentValidation;
using MediatR;

using Forge.Core.Interfaces.Communications;
using Forge.Core.Models.Communications;

namespace Forge.Api.Features.Communications;

/// <summary>
/// Wave 8 — generic ingest path that bypasses provider adapters. Useful
/// for: (1) Zapier / n8n / make.com style webhook bridges where the
/// upstream service does the provider translation, (2) admin / dev
/// testing of the matcher pipeline without standing up a real mailbox,
/// (3) one-off backfill scripts.
///
/// Provider-specific webhook receivers translate their payload to this
/// shape and POST through. Auth is the standard JWT — admins only.
/// </summary>
public record IngestCommunicationCommand(InboundCommunication Communication) : IRequest<CommunicationMatchResult>;

public class IngestCommunicationValidator : AbstractValidator<IngestCommunicationCommand>
{
    public IngestCommunicationValidator()
    {
        RuleFor(x => x.Communication).NotNull();
        RuleFor(x => x.Communication.ProviderId).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Communication.ExternalId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Communication.From).NotEmpty().MaximumLength(400);
        // To list can be empty for inbound where the routing was implicit
        // (a tenant's catch-all mailbox); validation is lenient on purpose.

        RuleFor(x => x.Communication.Kind)
            .Must(k => Enum.IsDefined(typeof(CommunicationKind), k))
            .WithMessage("Invalid communication kind");
        RuleFor(x => x.Communication.Direction)
            .Must(d => Enum.IsDefined(typeof(CommunicationDirection), d))
            .WithMessage("Invalid communication direction");
    }
}

public class IngestCommunicationHandler(ICommunicationMatcher matcher)
    : IRequestHandler<IngestCommunicationCommand, CommunicationMatchResult>
{
    public Task<CommunicationMatchResult> Handle(
        IngestCommunicationCommand request, CancellationToken cancellationToken)
        => matcher.MatchAndLogAsync(request.Communication, cancellationToken);
}
