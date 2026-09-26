namespace Observatory.Core;

/// <summary>HTTP contract shared by the shop business APIs and their clients.</summary>
public static class ShopHttpContract
{
    public const string CustomerHeader = "X-Observatory-Customer-Id";
    public const string ConfirmationHeader = "X-Observatory-Confirm-Action";
}

/// <summary>Body of the return assessment and return draft operations.</summary>
public sealed record ReturnOperationRequest(string OrderId, string Reason);
