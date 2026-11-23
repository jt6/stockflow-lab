using Microsoft.EntityFrameworkCore;
using StockFlow.Core;

namespace StockFlow.Infrastructure;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Product>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Sku).IsRequired().HasMaxLength(64);
            entity.Property(x => x.Name).IsRequired().HasMaxLength(200);
            entity.HasIndex(x => x.Sku).IsUnique();
        });
        model.Entity<Warehouse>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).IsRequired().HasMaxLength(64);
            entity.Property(x => x.Name).IsRequired().HasMaxLength(200);
            entity.HasIndex(x => x.Code).IsUnique();
        });
        model.Entity<StockMovement>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.CreatedAt).IsRequired();
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Movement_Quantity", "Quantity > 0");
                t.HasCheckConstraint("CK_Movement_Type", "Type IN (1, 2)");
            });
            entity.HasIndex(x => new { x.ProductId, x.WarehouseId });
        });
    }
}
