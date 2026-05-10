namespace SampleProject;

public sealed class InventoryService
{
    private readonly IReadOnlyDictionary<string, InventoryItem> _items;
    private readonly bool _backorderEnabled;

    public InventoryService(IEnumerable<InventoryItem> items, bool backorderEnabled)
    {
        _items = items.ToDictionary(item => item.Sku, StringComparer.OrdinalIgnoreCase);
        _backorderEnabled = backorderEnabled;
    }

    public InventoryResult GetStock(string sku)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("SKU is required.", nameof(sku));
        }

        if (!_items.TryGetValue(sku, out var item))
        {
            return InventoryResult.NotFound(sku);
        }

        return item.QuantityAvailable > 0
            ? InventoryResult.Available(item.Sku, item.QuantityAvailable)
            : InventoryResult.Unavailable(item.Sku, _backorderEnabled && item.BackorderAllowed);
    }

    public ReservationResult Reserve(string sku, int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        }

        var stock = GetStock(sku);
        if (!stock.Exists)
        {
            return ReservationResult.Rejected(sku, "SKU was not found.");
        }

        if (stock.QuantityAvailable >= quantity)
        {
            return ReservationResult.Accepted(sku, quantity);
        }

        return stock.BackorderAvailable
            ? ReservationResult.Backordered(sku, quantity)
            : ReservationResult.Rejected(sku, "Insufficient stock.");
    }
}
