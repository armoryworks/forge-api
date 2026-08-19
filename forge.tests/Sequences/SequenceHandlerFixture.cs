using MediatR;
using Moq;

using Forge.Api.Features.Sequences.GateSources;
using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Core.Sequences;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Sequences;

/// <summary>Wires the real evaluation service (real gate sources, InMemory db, fixed clock, capturing publisher) for handler tests.</summary>
public sealed class SequenceHandlerFixture : IAsyncDisposable
{
    public SequenceHandlerFixture(DateTimeOffset? now = null, params IGateSource[] extraSources)
    {
        Db = TestDbContextFactory.Create();
        Db.CurrentUserId = UserId;
        Clock = new MutableClock(now ?? new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero));
        Publisher = new Mock<IPublisher>();
        Publisher.Setup(p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()))
            .Callback<INotification, CancellationToken>((n, _) => Published.Add(n))
            .Returns(Task.CompletedTask);
        Sources.AddRange([new ManualClearanceGateSource(), new TimeWindowGateSource(), new ResourceClockGateSource(Db), new ApprovalGateSource(Db)]);
        Sources.AddRange(extraSources);
        // the service enumerates the live list, so tests may Sources.Add(...) after construction
        Evaluation = new SequenceEvaluationService(Db, Sources, Clock, Publisher.Object);
    }

    public const int UserId = 7;
    public AppDbContext Db { get; }
    public MutableClock Clock { get; }
    public Mock<IPublisher> Publisher { get; }
    public List<INotification> Published { get; } = [];
    public List<IGateSource> Sources { get; } = [];
    public ISequenceEvaluationService Evaluation { get; }

    public ValueTask DisposeAsync() => Db.DisposeAsync();

    public sealed class MutableClock(DateTimeOffset start) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = start;
        public void Advance(TimeSpan by) => UtcNow += by;
    }
}
