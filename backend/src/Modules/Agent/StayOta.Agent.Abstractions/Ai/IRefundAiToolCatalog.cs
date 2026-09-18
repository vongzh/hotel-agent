using Microsoft.Extensions.AI;
using StayOta.Agent.Abstractions.Contracts;

namespace StayOta.Agent.Abstractions.Ai;

public interface IRefundAiToolCatalog
{
    IReadOnlyList<AITool> GetAiTools(bool requireApprovalForWrites = true);
    IReadOnlyDictionary<string, AIFunction> Functions { get; }
    Task<ToolResult> InvokeAsync(ToolCall call, CancellationToken ct = default);
}
