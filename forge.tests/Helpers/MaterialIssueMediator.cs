using MediatR;
using Moq;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.Jobs;
using Forge.Data.Context;

namespace Forge.Tests.Helpers;

/// <summary>
/// An <see cref="IMediator"/> that runs the real material-issue and material-return handlers against the given
/// context, for handlers (the scanner issue and its reversal) that record material through them.
/// </summary>
public static class MaterialIssueMediator
{
    public static IMediator For(AppDbContext db, IMaterialIssuePostingService? posting = null)
    {
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<CreateMaterialIssueCommand>(), It.IsAny<CancellationToken>()))
            .Returns((CreateMaterialIssueCommand c, CancellationToken ct) =>
                new CreateMaterialIssueHandler(db, posting).Handle(c, ct));
        mediator
            .Setup(m => m.Send(It.IsAny<ReturnMaterialIssueCommand>(), It.IsAny<CancellationToken>()))
            .Returns((ReturnMaterialIssueCommand c, CancellationToken ct) =>
                new ReturnMaterialIssueHandler(db, posting).Handle(c, ct));
        return mediator.Object;
    }
}
