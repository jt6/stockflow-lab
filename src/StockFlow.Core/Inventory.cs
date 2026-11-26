namespace StockFlow.Core;

public sealed class Product
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class Warehouse
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}

public enum MovementType { In = 1, Out = 2 }

public sealed class StockMovement
{
    public long Id { get; set; }
    public int ProductId { get; set; }
    public int WarehouseId { get; set; }
    public MovementType Type { get; set; }
    public int Quantity { get; set; }
    public DateTime CreatedAt { get; set; }
}

public static class StockRules
{
    public static long Balance(IEnumerable<StockMovement> movements) =>
        movements.Sum(m => m.Type == MovementType.In ? (long)m.Quantity : -(long)m.Quantity);

    public static bool CanWithdraw(long available, int quantity) => quantity > 0 && available >= quantity;
}
