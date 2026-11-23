using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StockFlow.Core;
using StockFlow.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddDbContext<InventoryDbContext>((services, options) =>
    options.UseSqlite(services.GetRequiredService<IConfiguration>().GetConnectionString("Inventory")
        ?? "Data Source=stockflow.db;Default Timeout=30"));
builder.Services.AddScoped<MovementWriter>();

var app = builder.Build();
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.Migrate();

app.MapPost("/products", async (CreateProductRequest request, InventoryDbContext db, CancellationToken ct) =>
{
    var sku = request.Sku?.Trim();
    var name = request.Name?.Trim();
    if (string.IsNullOrWhiteSpace(sku) || sku.Length > 64 || string.IsNullOrWhiteSpace(name) || name.Length > 200)
        return Results.BadRequest(new ApiError("SKU and name are required (maximum lengths: 64 and 200)."));
    var product = new Product { Sku = sku, Name = name };
    db.Products.Add(product);
    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
    { return Results.Conflict(new ApiError("SKU already exists.")); }
    return Results.Created($"/products/{product.Id}/stock", new ProductResponse(product.Id, product.Sku, product.Name));
});

app.MapGet("/products", async (InventoryDbContext db, CancellationToken ct) =>
    Results.Ok(await db.Products.AsNoTracking().OrderBy(x => x.Id)
        .Select(x => new ProductResponse(x.Id, x.Sku, x.Name)).ToListAsync(ct)));

app.MapPost("/warehouses", async (CreateWarehouseRequest request, InventoryDbContext db, CancellationToken ct) =>
{
    var code = request.Code?.Trim();
    var name = request.Name?.Trim();
    if (string.IsNullOrWhiteSpace(code) || code.Length > 64 || string.IsNullOrWhiteSpace(name) || name.Length > 200)
        return Results.BadRequest(new ApiError("Code and name are required (maximum lengths: 64 and 200)."));
    var warehouse = new Warehouse { Code = code, Name = name };
    db.Warehouses.Add(warehouse);
    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
    { return Results.Conflict(new ApiError("Warehouse code already exists.")); }
    return Results.Created("/warehouses", new WarehouseResponse(warehouse.Id, warehouse.Code, warehouse.Name));
});

app.MapGet("/warehouses", async (InventoryDbContext db, CancellationToken ct) =>
    Results.Ok(await db.Warehouses.AsNoTracking().OrderBy(x => x.Id)
        .Select(x => new WarehouseResponse(x.Id, x.Code, x.Name)).ToListAsync(ct)));

app.MapPost("/stock-movements", async (CreateMovementRequest request, MovementWriter writer, CancellationToken ct) =>
{
    if (request.ProductId <= 0 || request.WarehouseId <= 0 || request.Quantity <= 0 ||
        request.Type is not (MovementType.In or MovementType.Out))
        return Results.BadRequest(new ApiError("ProductId, WarehouseId and Quantity must be positive; Type must be In or Out."));
    var (result, movement) = await writer.CreateAsync(request.ProductId, request.WarehouseId, request.Type, request.Quantity, ct);
    return result switch
    {
        MovementResult.ProductMissing => Results.NotFound(new ApiError("Product not found.")),
        MovementResult.WarehouseMissing => Results.NotFound(new ApiError("Warehouse not found.")),
        MovementResult.InsufficientStock => Results.Conflict(new ApiError("Insufficient stock.")),
        _ => Results.Created($"/products/{movement!.ProductId}/stock",
            new MovementResponse(movement.Id, movement.ProductId, movement.WarehouseId,
                movement.Type, movement.Quantity, movement.CreatedAt))
    };
});

app.MapGet("/products/{id:int}/stock", async (int id, InventoryDbContext db, CancellationToken ct) =>
{
    if (id <= 0) return Results.BadRequest(new ApiError("Product ID must be positive."));
    if (!await db.Products.AnyAsync(x => x.Id == id, ct))
        return Results.NotFound(new ApiError("Product not found."));
    var warehouses = await db.Warehouses.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
    var movements = await db.StockMovements.AsNoTracking().Where(x => x.ProductId == id).ToListAsync(ct);
    var balances = movements.GroupBy(x => x.WarehouseId)
        .ToDictionary(x => x.Key, x => StockRules.Balance(x));
    return Results.Ok(new ProductStockResponse(id, warehouses.Select(w =>
        new WarehouseStockResponse(w.Id, w.Code, balances.GetValueOrDefault(w.Id))).ToList()));
});

app.Run();

public sealed record CreateProductRequest(string? Sku, string? Name);
public sealed record CreateWarehouseRequest(string? Code, string? Name);
public sealed record CreateMovementRequest(int ProductId, int WarehouseId, MovementType Type, int Quantity);
public sealed record ProductResponse(int Id, string Sku, string Name);
public sealed record WarehouseResponse(int Id, string Code, string Name);
public sealed record MovementResponse(long Id, int ProductId, int WarehouseId, MovementType Type, int Quantity, DateTime CreatedAt);
public sealed record WarehouseStockResponse(int WarehouseId, string Code, long Quantity);
public sealed record ProductStockResponse(int ProductId, IReadOnlyList<WarehouseStockResponse> Warehouses);
public sealed record ApiError(string Error);
public partial class Program;
