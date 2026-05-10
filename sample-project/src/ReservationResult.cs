namespace SampleProject;

public sealed record ReservationResult(
    string Sku,
    int Quantity,
    string Status,
    string? Reason)
{
    public static ReservationResult Accepted(string sku, int quantity) =>
        new(sku, quantity, "accepted", null);

    public static ReservationResult Backordered(string sku, int quantity) =>
        new(sku, quantity, "backordered", null);

    public static ReservationResult Rejected(string sku, string reason) =>
        new(sku, 0, "rejected", reason);
}
