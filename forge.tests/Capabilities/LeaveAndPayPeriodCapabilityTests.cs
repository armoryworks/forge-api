using System.Reflection;

using FluentAssertions;

using Forge.Api.Capabilities;
using Forge.Api.Controllers;

namespace Forge.Tests.Capabilities;

public class LeaveAndPayPeriodCapabilityTests
{
    [Theory]
    [InlineData(nameof(LeaveController.UpdatePolicy))]
    [InlineData(nameof(LeaveController.GetAllBalances))]
    [InlineData(nameof(LeaveController.GetBalanceAdjustments))]
    [InlineData(nameof(LeaveController.AdjustBalance))]
    [InlineData(nameof(LeaveController.CancelRequest))]
    public void New_leave_endpoints_switch_off_with_the_leave_capability(string actionName)
    {
        Gate(typeof(LeaveController), actionName).Should().Be("CAP-HR-LEAVE");
    }

    [Fact]
    public void The_pay_period_preview_switches_off_with_time_tracking()
    {
        Gate(typeof(TimeTrackingController), nameof(TimeTrackingController.PreviewPayPeriods)).Should().Be("CAP-HR-TIMETRACK");
    }

    private static string? Gate(Type controller, string actionName)
    {
        var action = controller.GetMethod(actionName)!;
        action.GetCustomAttribute<CapabilityBootstrapAttribute>().Should().BeNull();
        controller.GetCustomAttribute<CapabilityBootstrapAttribute>().Should().BeNull();
        return (action.GetCustomAttribute<RequiresCapabilityAttribute>()
            ?? controller.GetCustomAttribute<RequiresCapabilityAttribute>())?.Capability;
    }
}
