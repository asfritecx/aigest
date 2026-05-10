namespace SampleProject;

public sealed record InventoryItem(
    string Sku,
    string Name,
    int QuantityAvailable,
    bool BackorderAllowed);
