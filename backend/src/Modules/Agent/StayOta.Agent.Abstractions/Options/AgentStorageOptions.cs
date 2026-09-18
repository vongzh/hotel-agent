namespace StayOta.Agent.Abstractions.Options;

/// <summary>
/// PostgreSQL storage isolation when sharing a StayOTA database.
/// </summary>
public sealed class AgentStorageOptions
{
    public const string SectionName = "AgentStorage";

    /// <summary>PG schema for Agent tables (e.g. agent_refund). Empty = public.</summary>
    public string Schema { get; set; } = "agent_refund";
}
