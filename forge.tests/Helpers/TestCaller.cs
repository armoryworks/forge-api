using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Moq;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Tests.Helpers;

/// <summary>
/// Signed-in callers for handler tests that read the user from <see cref="IHttpContextAccessor"/>,
/// and a <see cref="WorkedHoursService"/> over the seeded clock event types.
/// </summary>
public static class TestCaller
{
    public static IHttpContextAccessor As(int userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(h => h.HttpContext).Returns(() => new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
        });
        return accessor.Object;
    }

    public static IClock ClockAt(DateTimeOffset now)
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(now);
        return clock.Object;
    }

    public static WorkedHoursService WorkedHours(AppDbContext db, DateTimeOffset now)
    {
        var types = new Mock<IClockEventTypeService>();
        types.Setup(t => t.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new("ClockIn", "Clock In", "In", "ClockOut", "work", true, true, "login", "#22c55e"),
            new("ClockOut", "Clock Out", "Out", "ClockIn", "work", false, false, "logout", "#ef4444"),
            new("BreakStart", "Start Break", "OnBreak", "BreakEnd", "break", true, true, "free_breakfast", "#f59e0b"),
            new("BreakEnd", "End Break", "In", "BreakStart", "break", true, false, "play_arrow", "#22c55e"),
        ]);
        return new WorkedHoursService(db, types.Object, ClockAt(now));
    }
}
