using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Moq;

using Forge.Api.Features.Andon;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Andon;

public sealed class AndonOpenAlertsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 14, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task OpenOnly_LeavesOutResolvedAlerts_AndKeepsAcknowledgedOnes()
    {
        var press = new WorkCenter { Name = "Press", Code = "PRESS" };
        _db.WorkCenters.Add(press);
        await _db.SaveChangesAsync();
        _db.AndonAlerts.AddRange(
            new AndonAlert { WorkCenterId = press.Id, Type = AndonAlertType.Stoppage, Status = AndonAlertStatus.Active, RequestedById = 7, RequestedAt = Now.AddMinutes(-5) },
            new AndonAlert { WorkCenterId = press.Id, Type = AndonAlertType.Quality, Status = AndonAlertStatus.Acknowledged, RequestedById = 7, RequestedAt = Now.AddMinutes(-20), AcknowledgedById = 8, AcknowledgedAt = Now.AddMinutes(-15) },
            new AndonAlert { WorkCenterId = press.Id, Type = AndonAlertType.Material, Status = AndonAlertStatus.Resolved, RequestedById = 7, RequestedAt = Now.AddMinutes(-60), ResolvedById = 8, ResolvedAt = Now.AddMinutes(-30) });
        await _db.SaveChangesAsync();

        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(u => u.Users).Returns(_db.Users);
        var handler = new GetAndonAlertsHandler(_db, userManager.Object);

        var open = await handler.Handle(new GetAndonAlertsQuery(null, null, OpenOnly: true), CancellationToken.None);
        var all = await handler.Handle(new GetAndonAlertsQuery(null, null), CancellationToken.None);

        open.Select(a => a.Status).Should().Equal(AndonAlertStatus.Active, AndonAlertStatus.Acknowledged);
        all.Should().HaveCount(3);
    }
}
