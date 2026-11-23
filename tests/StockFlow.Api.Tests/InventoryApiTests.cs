using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace StockFlow.Api.Tests;

public sealed class InventoryApiTests
{
    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        public readonly string PathToDatabase = Path.Combine(Path.GetTempPath(), $"stockflow-test-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Inventory"] = $"Data Source={PathToDatabase};Default Timeout=30"
            }));

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            SqliteConnection.ClearAllPools();
            if (File.Exists(PathToDatabase)) File.Delete(PathToDatabase);
        }
    }

    [Fact]
    public async Task Creates_resources_and_keeps_warehouse_balances_separate()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var product = await CreateProduct(client, "SKU-1");
        var first = await CreateWarehouse(client, "A");
        var second = await CreateWarehouse(client, "B");

        Assert.Equal(HttpStatusCode.Created, (await Move(client, product.Id, first.Id, "In", 10)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Move(client, product.Id, second.Id, "In", 3)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Move(client, product.Id, first.Id, "Out", 4)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Move(client, product.Id, second.Id, "Out", 4)).StatusCode);

        var stock = await client.GetFromJsonAsync<ProductStockResponse>($"/products/{product.Id}/stock");
        Assert.NotNull(stock);
        Assert.Equal(6, stock.Warehouses.Single(x => x.WarehouseId == first.Id).Quantity);
        Assert.Equal(3, stock.Warehouses.Single(x => x.WarehouseId == second.Id).Quantity);
        Assert.Single((await client.GetFromJsonAsync<List<ProductResponse>>("/products"))!);
        Assert.Equal(2, (await client.GetFromJsonAsync<List<WarehouseResponse>>("/warehouses"))!.Count);
    }

    [Fact]
    public async Task Rejects_duplicates_invalid_requests_and_missing_resources()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var product = await CreateProduct(client, "SKU-1");
        var warehouse = await CreateWarehouse(client, "A");

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/products", new { sku = "SKU-1", name = "Copy" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/warehouses", new { code = "A", name = "Copy" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/products", new { sku = " ", name = "Name" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Move(client, product.Id, warehouse.Id, "In", 0)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Move(client, product.Id, warehouse.Id, "Invalid", 1)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Move(client, 9999, warehouse.Id, "In", 1)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Move(client, product.Id, 9999, "In", 1)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/products/9999/stock")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Move(client, product.Id, warehouse.Id, "Out", 1)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_exits_cannot_overdraw_stock()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var product = await CreateProduct(client, "SKU-1");
        var warehouse = await CreateWarehouse(client, "A");
        Assert.Equal(HttpStatusCode.Created, (await Move(client, product.Id, warehouse.Id, "In", 1)).StatusCode);

        var exits = await Task.WhenAll(
            Move(client, product.Id, warehouse.Id, "Out", 1),
            Move(client, product.Id, warehouse.Id, "Out", 1));
        Assert.Single(exits, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(exits, x => x.StatusCode == HttpStatusCode.Conflict);
        var stock = await client.GetFromJsonAsync<ProductStockResponse>($"/products/{product.Id}/stock");
        Assert.Equal(0, stock!.Warehouses.Single().Quantity);
    }

    [Fact]
    public async Task Persisted_movements_cannot_be_updated_or_deleted()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var product = await CreateProduct(client, "SKU-1");
        var warehouse = await CreateWarehouse(client, "A");
        Assert.Equal(HttpStatusCode.Created, (await Move(client, product.Id, warehouse.Id, "In", 2)).StatusCode);
        using var connection = new SqliteConnection($"Data Source={factory.PathToDatabase}");
        await connection.OpenAsync();
        using var update = connection.CreateCommand();
        update.CommandText = "UPDATE StockMovements SET Quantity = 100 WHERE Id = 1";
        await Assert.ThrowsAsync<SqliteException>(() => update.ExecuteNonQueryAsync());
        using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM StockMovements WHERE Id = 1";
        await Assert.ThrowsAsync<SqliteException>(() => delete.ExecuteNonQueryAsync());
    }

    private static async Task<ProductResponse> CreateProduct(HttpClient client, string sku)
    {
        var response = await client.PostAsJsonAsync("/products", new { sku, name = "Product" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private static async Task<WarehouseResponse> CreateWarehouse(HttpClient client, string code)
    {
        var response = await client.PostAsJsonAsync("/warehouses", new { code, name = "Warehouse" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<WarehouseResponse>())!;
    }

    private static Task<HttpResponseMessage> Move(HttpClient client, int productId, int warehouseId, string type, int quantity) =>
        client.PostAsJsonAsync("/stock-movements", new { productId, warehouseId, type, quantity });
}
