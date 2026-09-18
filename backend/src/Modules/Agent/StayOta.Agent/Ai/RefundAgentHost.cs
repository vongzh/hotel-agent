using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StayOta.Agent.Abstractions.Ai;

namespace StayOta.Agent.Ai;

/// <summary>
/// Hosts a Microsoft Agent Framework <see cref="ChatClientAgent"/> backed by MEAI tools.
/// </summary>
public sealed class RefundAgentHost : IRefundAgentHost
{
    public RefundAgentHost(
        IChatClient chatClient,
        IRefundAiToolCatalog tools,
        ILoggerFactory loggerFactory,
        IServiceProvider services)
    {
        ChatClient = chatClient;
        ProviderName = chatClient.GetService<ChatClientMetadata>()?.ProviderName
                       ?? chatClient.GetType().Name;

        var pipeline = chatClient.AsBuilder()
            .UseFunctionInvocation(loggerFactory)
            .Build(services);

        Agent = new ChatClientAgent(
            pipeline,
            instructions: """
                你是 StayOTA 酒店退款助手。遵循政策与风险分层，优先调用已注册工具完成查单、报价与受控写操作。
                高风险（L3）财务写操作必须人工确认；不得绕过确认门禁。
                写操作工具可能需要人工审批（ApprovalRequired）。
                """,
            name: "stayota-refund-agent",
            description: "Hotel refund agent powered by Microsoft.Extensions.AI + Agent Framework",
            tools: tools.GetAiTools(requireApprovalForWrites: true).ToList(),
            loggerFactory: loggerFactory,
            services: services);
    }

    public AIAgent Agent { get; }
    public IChatClient ChatClient { get; }
    public string ProviderName { get; }
}
