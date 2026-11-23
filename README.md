# StockFlow

Baseline de una API REST de inventario con .NET 10, ASP.NET Core Minimal APIs, EF Core 10 y SQLite. La solución contiene `StockFlow.Api`, `StockFlow.Core`, `StockFlow.Infrastructure` y dos proyectos xUnit.

## Requisitos

- SDK de .NET 10 (`dotnet --version`).
- Acceso a NuGet durante la primera restauración.
- `dotnet-ef` 10 para aplicar migraciones desde la línea de comandos: `dotnet tool install --global dotnet-ef --version 10.0.11` (o `dotnet tool update --global dotnet-ef --version 10.0.11`).

Desde la raíz del repositorio:

```powershell
dotnet restore StockFlow.slnx
dotnet build StockFlow.slnx --no-restore
dotnet ef database update --project src/StockFlow.Infrastructure --startup-project src/StockFlow.Api
dotnet run --project src/StockFlow.Api --no-restore
dotnet test StockFlow.slnx --no-restore
```

La base de datos predeterminada es `stockflow.db` en el directorio de trabajo del proceso. La aplicación aplica las migraciones pendientes al arrancar. Para usar otra ubicación, configure `ConnectionStrings__Inventory`, por ejemplo:

```powershell
$env:ConnectionStrings__Inventory = 'Data Source=C:\temp\stockflow.db;Default Timeout=30'
dotnet run --project src/StockFlow.Api
```

Para `dotnet ef database update`, use `STOCKFLOW_CONNECTION_STRING` para indicar otra ruta a la factoría de diseño.

## API

| Método | Ruta | Cuerpo o resultado |
| --- | --- | --- |
| POST | `/products` | `{ "sku": "SKU-1", "name": "Producto" }` → 201 |
| GET | `/products` | Lista de productos → 200 |
| POST | `/warehouses` | `{ "code": "MAD", "name": "Madrid" }` → 201 |
| GET | `/warehouses` | Lista de almacenes → 200 |
| POST | `/stock-movements` | `{ "productId": 1, "warehouseId": 1, "type": "In", "quantity": 5 }` → 201 |
| GET | `/products/{id}/stock` | Stock por almacén, incluidos los de saldo cero → 200 |

`type` admite `In` y `Out`. Los errores de validación devuelven 400, los recursos ausentes 404 y los duplicados o salidas sin saldo 409, con un objeto `error`. No hay endpoints de edición ni eliminación de movimientos; la migración también impide UPDATE y DELETE sobre esa tabla mediante triggers.

## Regla de concurrencia

Cada movimiento inicia una transacción de escritura de SQLite antes de leer el saldo. SQLite serializa a los escritores incluso cuando la API corre en varios procesos que comparten el archivo; así, dos salidas no pueden aprobarse con el mismo saldo previo. El tiempo de espera de bloqueo predeterminado es 30 segundos. Las pruebas de integración usan archivos SQLite independientes y ejecutan salidas simultáneas.

## Limitaciones conocidas

- SQLite admite un escritor a la vez para toda la base de datos. La carga alta de escrituras puede producir esperas o errores de bloqueo; este diseño está pensado como baseline local, no para alta escala.
- El cálculo del endpoint de stock carga los movimientos del producto en memoria. Para un historial grande convendrá agregar en SQL o usar una proyección mantenida transaccionalmente.
- SKU y código se comparan con la colación predeterminada de SQLite, sensible a mayúsculas y minúsculas. No se implementan reglas de normalización adicionales.
