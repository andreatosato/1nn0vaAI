using System.Net;
using System.Net.Http.Json;
using Observatory.Core;
using ReturnsApi = Observatory.Shop.Returns.DomainExceptionHandler;
using static Observatory.Shop.Tests.ShopApi<Observatory.Shop.Returns.DomainExceptionHandler>;

namespace Observatory.Shop.Tests;

public sealed class ReturnsApiTests(ShopApi<ReturnsApi> api) : IClassFixture<ShopApi<ReturnsApi>>
{
    [Fact]
    public async Task Policies_are_served_in_priority_order()
    {
        var policies = await api.CreateClient().GetFromJsonAsync<ShopPolicy[]>("/policies", Json);

        Assert.Equal(policies!.OrderByDescending(policy => policy.Priority), policies);
    }

    [Fact]
    public async Task A_defect_is_eligible_and_refunds_the_amount_paid()
    {
        using var response = await api.Customer()
            .PostAsJsonAsync("/return-assessments", new ReturnOperationRequest("ORD-1042", "defect"), Json);
        var assessment = await response.Content.ReadFromJsonAsync<ReturnAssessment>(Json);

        Assert.True(assessment!.Eligible);
        Assert.Equal(19.99m, assessment.RefundAmount);
        Assert.Equal("POL-DEFECT-60", assessment.PolicyId);
    }

    // The contract the measured runs tripped on: free text is rejected, only the three reason codes are valid.
    [Fact]
    public async Task Free_text_reasons_are_rejected_with_the_valid_codes()
    {
        using var response = await api.Customer()
            .PostAsJsonAsync("/return-assessments", new ReturnOperationRequest("ORD-1042", "difettoso al primo utilizzo"), Json);

        await AssertProblem(response, HttpStatusCode.BadRequest, "invalid_return_reason");
    }

    [Fact]
    public async Task Assessments_require_a_trusted_customer_context()
    {
        using var response = await api.Customer(null)
            .PostAsJsonAsync("/return-assessments", new ReturnOperationRequest("ORD-1042", "defect"), Json);

        await AssertProblem(response, HttpStatusCode.Unauthorized, "customer_context_required");
    }
}
