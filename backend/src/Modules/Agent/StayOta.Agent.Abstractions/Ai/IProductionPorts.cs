using Microsoft.Extensions.AI;

namespace StayOta.Agent.Abstractions.Ai;

public interface IProductionOrderClient
{
    string Mode { get; }
    Task<object?> GetOrderDetailAsync(string orderId, string userId, CancellationToken ct = default);
    Task<object?> ListUserOrdersAsync(string userId, CancellationToken ct = default);
    Task<object?> GetPolicySnapshotAsync(string policyId, string orderId, CancellationToken ct = default);
    Task<object?> GetRefundStatusAsync(string refundId, CancellationToken ct = default);
}

public interface IExternalMcpToolSource
{
    Task<IReadOnlyList<AITool>> ListToolsAsync(CancellationToken ct = default);
}
