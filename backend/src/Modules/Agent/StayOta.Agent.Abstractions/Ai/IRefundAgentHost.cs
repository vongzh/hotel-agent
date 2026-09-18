using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace StayOta.Agent.Abstractions.Ai;

public interface IRefundAgentHost
{
    AIAgent Agent { get; }
    IChatClient ChatClient { get; }
    string ProviderName { get; }
}
