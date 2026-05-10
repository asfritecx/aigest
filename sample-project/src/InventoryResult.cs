namespace SampleProject;

public sealed record InventoryResult(
    string Sku,
    bool Exists,
    int QuantityAvailable,
    bool BackorderAvailable)
{
    public static InventoryResult Available(string sku, int quantity) =>
        new(sku, true, quantity, false);

    public static InventoryResult Unavailable(string sku, bool backorderAvailable) =>
        new(sku, true, 0, backorderAvailable);

    public static InventoryResult NotFound(string sku) =>
        new(sku, false, 0, false);
}
