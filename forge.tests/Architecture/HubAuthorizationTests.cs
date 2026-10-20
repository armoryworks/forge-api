using System.Reflection;

using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

using Forge.Api.Hubs;

namespace Forge.Tests.Architecture;

public class HubAuthorizationTests
{
    private static readonly HashSet<Type> ScopedByMembershipOrUser =
    [
        typeof(NotificationHub),
        typeof(TimerHub),
        typeof(ChatHub),
        typeof(AccountingHub),
    ];

    [Fact]
    public void Every_hub_requires_a_signed_in_user()
    {
        foreach (var hub in Hubs())
            hub.GetCustomAttributes<AuthorizeAttribute>(true).Should().NotBeEmpty($"{hub.Name} must carry [Authorize]");
    }

    [Fact]
    public void A_hub_open_to_any_signed_in_user_gates_its_group_joins_or_is_scoped_by_its_own_check()
    {
        var unscoped = Hubs()
            .Where(h => !ScopedByMembershipOrUser.Contains(h))
            .Where(h => h.GetCustomAttributes<AuthorizeAttribute>(true).All(a => string.IsNullOrEmpty(a.Roles)))
            .SelectMany(h => h.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.Name.StartsWith("Join", StringComparison.Ordinal))
                .Where(m => m.GetCustomAttributes<AuthorizeAttribute>().All(a => string.IsNullOrEmpty(a.Roles)))
                .Select(m => $"{h.Name}.{m.Name}"))
            .ToList();

        unscoped.Should().BeEmpty();
    }

    private static IEnumerable<Type> Hubs()
        => typeof(BoardHub).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(Hub).IsAssignableFrom(t));
}
