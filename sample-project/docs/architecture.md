# Inventory API Architecture

The Inventory API exposes read-only inventory lookup and reservation workflows for internal tools.

## Components

- `src/InventoryService.cs` contains stock lookup, reservation checks, and backorder behavior.
- `src/InventoryRoutes.cs` maps HTTP-style endpoint handlers to service methods.
- `config/appsettings.json` contains local ports, feature flags, and external dependency URLs.

## Data Flow

1. A caller requests stock for a SKU.
2. The route layer validates the SKU and calls `InventoryService`.
3. The service checks local inventory records.
4. If stock is unavailable and backorders are enabled, the service returns a backorder response.

## Operational Notes

The service depends on catalog data and optional warehouse synchronization. Warehouse synchronization is disabled in the local sample config.
