using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using StayOta.Agent.Abstractions.Ai;
using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Domain;

namespace StayOta.Agent.Ai;

public sealed class AgentConversationService(
    IRefundAgentHost agentHost,
    IAgentSessionStore sessionStore,
    ILogger<AgentConversationService> logger) : IAgentConversationService
{
    public async Task<AgentTurnResult> RunTurnAsync(AgentTurnRequest request, CancellationToken ct = default)
    {
        using var _ = ToolInvocationContext.Push(new ToolInvocationContext
        {
            TraceId = request.TraceId,
            UserId = request.UserId,
            OrderId = request.OrderId,
            CaseId = request.CaseId,
            RiskLevel = request.RiskLevel,
            ConversationState = request.ConversationState,
            Arguments = request.AmbientArguments,
            ConfirmationToken = request.ConfirmationToken,
            IdempotencyKey = request.IdempotencyKey,
            ExpectedOrderVersion = request.ExpectedOrderVersion,
            Access = ToolAccess.Read
        });

        var planned = request.PlannedTools.ToList();
        if (request.RequireWriteApproval && !string.IsNullOrWhiteSpace(request.WriteToolName))
            planned.Add(request.WriteToolName!);

        DeterministicRefundChatClient.PlannedTools.Value = planned.Distinct().ToList();
        DeterministicRefundChatClient.FinalReply.Value = request.SuggestedReply;

        var session = await agentHost.Agent.CreateSessionAsync(ct);
        var sessionId = $"ags_{Guid.NewGuid():N}"[..20];

        AgentResponse response;
        try
        {
            response = await agentHost.Agent.RunAsync(request.Message, session, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Agent turn failed; falling back to suggested reply");
            return new AgentTurnResult(sessionId, request.SuggestedReply, false, [], [], false);
        }

        var pending = ExtractApprovals(response);
        var toolsInvoked = ExtractInvokedTools(response);
        var reply = string.IsNullOrWhiteSpace(response.Text) ? request.SuggestedReply : response.Text;

        var sessionJson = await agentHost.Agent.SerializeSessionAsync(session, cancellationToken: ct);
        await sessionStore.SaveAsync(sessionId, new AgentSessionSnapshot
        {
            SessionJson = sessionJson.GetRawText(),
            TraceId = request.TraceId,
            UserId = request.UserId,
            OrderId = request.OrderId,
            CaseId = request.CaseId,
            ScenarioId = request.ScenarioId,
            RiskLevel = request.RiskLevel.ToString(),
            ConversationState = request.ConversationState,
            ConfirmationToken = request.ConfirmationToken,
            IdempotencyKey = request.IdempotencyKey,
            ExpectedOrderVersion = request.ExpectedOrderVersion,
            AmbientArguments = new Dictionary<string, object?>(request.AmbientArguments),
            PendingApprovals = pending.Select(p => new PendingApprovalRecord
            {
                RequestId = p.RequestId,
                CallId = p.CallId,
                ToolName = p.ToolName,
                Arguments = p.Arguments.ToDictionary(kv => kv.Key, kv => kv.Value)
            }).ToList()
        }, ct);

        logger.LogInformation(
            "Agent turn session={Session} pendingApprovals={Count} tools={Tools}",
            sessionId, pending.Count, string.Join(',', toolsInvoked));

        return new AgentTurnResult(sessionId, reply, pending.Count > 0, pending, toolsInvoked, true);
    }

    public async Task<AgentTurnResult> RespondToApprovalAsync(ApprovalResponseRequest request, CancellationToken ct = default)
    {
        var snapshot = await sessionStore.GetAsync(request.SessionId, ct)
                       ?? throw new InvalidOperationException("agent session not found or expired");

        var pending = snapshot.PendingApprovals.FirstOrDefault(p => p.RequestId == request.RequestId)
                      ?? throw new InvalidOperationException($"approval request {request.RequestId} not found");

        using var _ = ToolInvocationContext.Push(new ToolInvocationContext
        {
            TraceId = snapshot.TraceId,
            UserId = snapshot.UserId,
            OrderId = snapshot.OrderId,
            CaseId = snapshot.CaseId,
            RiskLevel = Enum.TryParse<RiskLevel>(snapshot.RiskLevel, out var rl) ? rl : RiskLevel.L1,
            ConversationState = snapshot.ConversationState,
            Arguments = snapshot.AmbientArguments,
            ConfirmationToken = snapshot.ConfirmationToken,
            IdempotencyKey = snapshot.IdempotencyKey,
            ExpectedOrderVersion = snapshot.ExpectedOrderVersion,
            Access = ToolAccess.Write
        });

        var session = await agentHost.Agent.DeserializeSessionAsync(
            JsonDocument.Parse(snapshot.SessionJson).RootElement, cancellationToken: ct);

        var functionCall = new FunctionCallContent(
            pending.CallId,
            pending.ToolName,
            pending.Arguments.ToDictionary(kv => kv.Key, kv => (object?)(kv.Value ?? "")));
        var approvalRequest = new ToolApprovalRequestContent(pending.RequestId, functionCall);
        var approvalMessage = new ChatMessage(ChatRole.User,
            [approvalRequest.CreateResponse(request.Approved, request.Reason ?? (request.Approved ? "user approved" : "user rejected"))]);

        DeterministicRefundChatClient.PlannedTools.Value = [];
        DeterministicRefundChatClient.FinalReply.Value = request.Approved
            ? $"已批准执行 {pending.ToolName}，业务写操作已提交。"
            : $"已拒绝执行 {pending.ToolName}，未改变业务状态。";

        var response = await agentHost.Agent.RunAsync(approvalMessage, session, cancellationToken: ct);
        var nextPending = ExtractApprovals(response);
        var toolsInvoked = ExtractInvokedTools(response);
        var reply = string.IsNullOrWhiteSpace(response.Text)
            ? DeterministicRefundChatClient.FinalReply.Value!
            : response.Text;

        var sessionJson = await agentHost.Agent.SerializeSessionAsync(session, cancellationToken: ct);
        snapshot.SessionJson = sessionJson.GetRawText();
        snapshot.PendingApprovals = nextPending.Select(p => new PendingApprovalRecord
        {
            RequestId = p.RequestId,
            CallId = p.CallId,
            ToolName = p.ToolName,
            Arguments = p.Arguments.ToDictionary(kv => kv.Key, kv => kv.Value)
        }).ToList();
        await sessionStore.SaveAsync(request.SessionId, snapshot, ct);

        return new AgentTurnResult(request.SessionId, reply, nextPending.Count > 0, nextPending, toolsInvoked, true);
    }

    private static List<PendingToolApprovalDto> ExtractApprovals(AgentResponse response)
    {
        var list = new List<PendingToolApprovalDto>();
        foreach (var content in response.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>())
        {
            if (content.ToolCall is not FunctionCallContent call) continue;
            var args = call.Arguments?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? new Dictionary<string, object?>();
            list.Add(new PendingToolApprovalDto(
                content.RequestId,
                call.CallId ?? content.RequestId,
                call.Name ?? "unknown",
                args,
                $"批准执行 Tool `{call.Name}`（官方 ToolApprovalRequestContent）"));
        }
        return list;
    }

    private static List<string> ExtractInvokedTools(AgentResponse response)
    {
        return response.Messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionCallContent>()
            .Select(c => c.Name ?? "")
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct()
            .ToList();
    }
}
