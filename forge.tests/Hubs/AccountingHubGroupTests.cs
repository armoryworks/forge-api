using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

using Forge.Api.Authorization;
using Forge.Api.Features.Accounting;
using Forge.Api.Hubs;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Hubs;

public class AccountingHubGroupTests
{
    private static (AccountingHub Hub, Mock<IGroupManager> Groups) Connect(params string[] roles)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            roles.Select(r => new Claim(ClaimTypes.Role, r)).Append(new Claim(ClaimTypes.NameIdentifier, "7")),
            "test"));
        var context = new Mock<HubCallerContext>();
        context.Setup(c => c.User).Returns(user);
        context.Setup(c => c.ConnectionId).Returns("conn-1");
        var groups = new Mock<IGroupManager>();
        var hub = new AccountingHub { Context = context.Object, Groups = groups.Object };
        return (hub, groups);
    }

    [Theory]
    [InlineData("Controller")]
    [InlineData("Admin")]
    [InlineData("Manager")]
    [InlineData("OfficeManager")]
    [InlineData("Procurement")]
    public async Task A_role_with_an_accounting_screen_joins_the_accounting_group(string role)
    {
        var (hub, groups) = Connect(role);

        await hub.OnConnectedAsync();

        groups.Verify(g => g.AddToGroupAsync("conn-1", AccountingRoles.Group, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("ProductionWorker")]
    [InlineData("Engineer")]
    [InlineData("PM")]
    public async Task Any_other_role_connects_but_joins_no_group(string role)
    {
        var (hub, groups) = Connect(role);

        await hub.OnConnectedAsync();

        groups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_ledger_write_signals_the_accounting_group_and_not_every_client()
    {
        var proxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(AccountingRoles.Group)).Returns(proxy.Object);
        var hub = new Mock<IHubContext<AccountingHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
            .ConfigureWarnings(w =>
            {
                w.Ignore(InMemoryEventId.TransactionIgnoredWarning);
                w.Log(CoreEventId.ManyServiceProvidersCreatedWarning);
            })
            .AddInterceptors(new GlChangeBroadcastInterceptor(hub.Object))
            .Options;
        await using var db = new TestAppDbContext(options);
        db.Add(new GlAccount
        {
            BookId = 1, AccountNumber = "10100", Name = "Cash",
            AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit,
        });

        await db.SaveChangesAsync();

        proxy.Verify(p => p.SendCoreAsync("accountingChanged", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
        clients.Verify(c => c.All, Times.Never);
    }
}
