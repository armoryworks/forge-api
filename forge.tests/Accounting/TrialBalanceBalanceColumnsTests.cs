using FluentAssertions;

using Forge.Core.Models.Accounting;

namespace Forge.Tests.Accounting;

public class TrialBalanceBalanceColumnsTests
{
    [Fact]
    public void Rows_PutTheNetBalanceOnOneSide_AndTotalsTie()
    {
        var cash = new TrialBalanceRow { AccountNumber = "10100", DebitTotal = 1500m, CreditTotal = 400m };
        var sales = new TrialBalanceRow { AccountNumber = "40000", DebitTotal = 100m, CreditTotal = 1200m };
        var cleared = new TrialBalanceRow { AccountNumber = "10150", DebitTotal = 50m, CreditTotal = 50m };
        var tb = new TrialBalance
        {
            Rows = [cash, sales, cleared],
            TotalDebit = 1650m,
            TotalCredit = 1650m,
        };

        cash.DebitBalance.Should().Be(1100m);
        cash.CreditBalance.Should().Be(0m);
        sales.DebitBalance.Should().Be(0m);
        sales.CreditBalance.Should().Be(1100m);
        cleared.DebitBalance.Should().Be(0m);
        cleared.CreditBalance.Should().Be(0m);
        tb.TotalDebitBalance.Should().Be(1100m);
        tb.TotalCreditBalance.Should().Be(1100m);
        tb.IsBalanced.Should().BeTrue();
    }
}
