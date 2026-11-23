using Microsoft.EntityFrameworkCore;
using StockFlow.Core;

namespace StockFlow.Infrastructure;

public enum MovementResult { Created, ProductMissing, WarehouseMissing, InsufficientStock }

public sealed class MovementWriter(InventoryDbContext db)
{
    public async Task<(MovementResult Result, StockMovement? Movement)> CreateAsync(
        int productId, int warehouseId, MovementType type, int quantity, CancellationToken cancellationToken)
    {
        // SQLite starts a write transaction here (BEGIN IMMEDIATE). The balance read and
        // insert are serialized across connections and processes sharing this database.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await db.Products.AnyAsync(x => x.Id == productId, cancellationToken))
            return (MovementResult.ProductMissing, null);
        if (!await db.Warehouses.AnyAsync(x => x.Id == warehouseId, cancellationToken))
            return (MovementResult.WarehouseMissing, null);

        if (type == MovementType.Out)
        {
            var available = await db.StockMovements
                .Where(x => x.ProductId == productId && x.WarehouseId == warehouseId)
                .SumAsync(x => x.Type == MovementType.In ? (long)x.Quantity : -(long)x.Quantity, cancellationToken);
            if (!StockRules.CanWithdraw(available, quantity))
                return (MovementResult.InsufficientStock, null);
        }

        var movement = new StockMovement
        {
            ProductId = productId, WarehouseId = warehouseId, Type = type,
            Quantity = quantity, CreatedAt = DateTime.UtcNow
        };
        db.StockMovements.Add(movement);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (MovementResult.Created, movement);
    }
}
