namespace SampleProject;

public static class InventoryRoutes
{
    public static InventoryResult GetStock(InventoryService service, string sku)
    {
        return service.GetStock(sku);
    }

    public static ReservationResult Reserve(InventoryService service, string sku, int quantity)
    {
        return service.Reserve(sku, quantity);
    }
}
