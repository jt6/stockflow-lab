using StockFlow.Core;

namespace StockFlow.Core.Tests;

public sealed class StockRulesTests
{
    [Fact]
    public void Balance_subtracts_outgoing_movements()
    {
        var movements = new[]
        {
            new StockMovement { Type = MovementType.In, Quantity = 9 },
            new StockMovement { Type = MovementType.Out, Quantity = 4 },
            new StockMovement { Type = MovementType.In, Quantity = 2 }
        };
        Assert.Equal(7, StockRules.Balance(movements));
    }

    [Theory]
    [InlineData(3, 4, false)]
    [InlineData(3, 3, true)]
    [InlineData(3, 0, false)]
    public void Withdrawal_respects_available_stock(long available, int quantity, bool expected) =>
        Assert.Equal(expected, StockRules.CanWithdraw(available, quantity));
}
