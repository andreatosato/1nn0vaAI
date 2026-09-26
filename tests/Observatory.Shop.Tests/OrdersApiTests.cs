using System.Net;
using System.Net.Http.Json;
using Observatory.Core;
using OrdersApi = Observatory.Shop.Orders.DomainExceptionHandler;
using static Observatory.Shop.Tests.ShopApi<Observatory.Shop.Orders.DomainExceptionHandler>;

namespace Observatory.Shop.Tests;

public sealed class OrdersApiTests(ShopApi<OrdersApi> api) : IClassFixture<ShopApi<OrdersApi>>
{
    [Fact]
    public async Task Order_distinguishes_the_amount_paid_from_the_list_price()
    {
        var order = await api.Customer().GetFromJsonAsync<ShopOrder>("/orders/ORD-1042", Json);

        Assert.Equal((19.99m, 29.99m), (order!.AmountPaid, order.ListPrice));
    }

    [Fact]
    public async Task Teaching_endpoint_lists_all_synthetic_orders_without_a_customer()
    {
        var orders = await api.CreateClient().GetFromJsonAsync<ShopOrder[]>("/demo-data/orders", Json);

        Assert.Equal(50, orders!.Length);
        Assert.Contains(orders, order => order.CustomerId != DemoClock.CustomerId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bad customer")]
    public async Task Orders_require_a_trusted_customer_context(string? customer)
    {
        using var response = await api.Customer(customer).GetAsync("/orders/ORD-1042");

        await AssertProblem(response, HttpStatusCode.Unauthorized, "customer_context_required");
    }

    [Fact]
    public async Task Other_customers_orders_are_not_found()
    {
        using var response = await api.Customer().GetAsync("/orders/ORD-1001");

        await AssertProblem(response, HttpStatusCode.NotFound, "order_not_found");
        Assert.DoesNotContain("CUST-DEMO-02", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Drafts_require_the_server_confirmation_header()
    {
        using var response = await api.Customer(confirmed: false)
            .PostAsJsonAsync("/return-drafts", new ReturnOperationRequest("ORD-1042", "defect"), Json);

        await AssertProblem(response, HttpStatusCode.Forbidden, "confirmation_required");
    }
}
