using Observatory.Core;

namespace Observatory.AgentRuntime;

public static class AgentNames
{
    public const string Router = "router";
    public const string Catalog = "catalog";
    public const string Orders = "orders";
    public const string Returns = "returns";
    public static readonly string[] All = [Router, Catalog, Orders, Returns];
    public static readonly string[] Specialists = [Catalog, Orders, Returns];

    public static IReadOnlyList<string> ForTechnology(string technology) => technology switch
    {
        DemoTechnologies.Inline or DemoTechnologies.Skills => [Router],
        DemoTechnologies.A2A => All,
        _ => throw new DomainException("unknown_technology", "Tecnologia non supportata.")
    };
}
