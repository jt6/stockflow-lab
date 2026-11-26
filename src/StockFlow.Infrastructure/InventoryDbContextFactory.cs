using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StockFlow.Infrastructure;

public sealed class InventoryDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("STOCKFLOW_CONNECTION_STRING") ?? "Data Source=stockflow.db;Default Timeout=30";
        return new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite(connection).Options);
    }
}
