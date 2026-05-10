using SampleProject;

namespace SampleProject.Tests;

public sealed class InventoryServiceTests
{
    [Fact]
    public void Reserve_ReturnsBackorder_WhenStockUnavailableAndBackorderEnabled()
    {
        var service = new InventoryService(
            [new InventoryItem("SKU-1", "Sample", 0, BackorderAllowed: true)],
            backorderEnabled: true);

        var result = service.Reserve("SKU-1", 2);

        Assert.Equal("backordered", result.Status);
    }
}
