using System.Text.Json;
using Microsoft.Extensions.Logging;
using StayOta.Agent.Abstractions.Ai;
using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Domain;
using StayOta.Agent.Abstractions.Domain.Entities;
using StayOta.Agent.Abstractions.Options;

namespace StayOta.Agent.Plugins.Refund.Services;

public sealed class AgentOrchestrator(
    IRefundDataStore store,
    IIntentService intentService,
    IPolicyRetrieval retrieval,
    IRulesEngine rules,
    IRefundAiToolCatalog tools,
    IRefundAgentHost agentHost,
    IAgentConversationService conversation,
    IAgentSessionStore agentSessionStore,
    IConfirmationStore confirmationStore,
    ISessionStore sessionStore,
    IVerifier verifier,
    IProductionOrderClient production,
    Microsoft.Extensions.Options.IOptions<HostingOptions> hostingOptions,
    ILogger<AgentOrchestrator> logger) : IAgentOrchestrator
{
    private readonly ScenarioRouter _router = new();

    public async Task<AgentDecisionDto> HandleAsync(AgentMessageRequest request, CancellationToken ct = default)
    {
        if (request.ServiceError)
            throw new InvalidOperationException("模拟订单服务响应超时");

        var hosting = hostingOptions.Value;
        if (request.ResetDemo)
        {
            if (!hosting.DemoEnabled)
                throw new InvalidOperationException("ResetDemo is disabled outside demo mode");
            await store.ResetDemoAsync(ct);
        }
        else await store.EnsureSeededAsync(ct);

        var scenarioId = _router.Route(request.Message, request.ScenarioId);
        var scenario = store.GetScenario(scenarioId);
        var order = await store.GetOrderAsync(scenario.OrderId, ct)
                    ?? throw new InvalidOperationException($"missing order {scenario.OrderId}");
        var policy = await store.GetPolicyAsync(order.PolicyId, ct)
                     ?? new PolicySnapshot { PolicyId = order.PolicyId, Title = "默认政策", Summary = "演示", RuleCode = "DEFAULT" };

        var signals = new AgentSignals(request.HasEvidence, request.HasNegotiationReason, request.LowConfidence, request.ServiceError, request.ConfirmWrite);
        var traceId = $"trc_{Guid.NewGuid():N}"[..16];
        var runId = $"run_{Guid.NewGuid():N}"[..16];
        var userId = request.UserId ?? scenario.UserId;
        var steps = new List<DecisionStepDto>();

        var analyzed = intentService.Analyze(request.Message, scenario, request.LowConfidence);
        steps.Add(new("意图识别", analyzed.Confidence < 0.5 ? "warning" : "success", analyzed.Intent, analyzed.Confidence));

        if (request.HasEvidence) analyzed.Slots["evidence"] = "uploaded";
        if (request.HasNegotiationReason) analyzed.Slots["negotiation_reason"] = "provided";
        var missing = (signals.HasEvidence || !NeedsEvidence(scenario.ScenarioId, signals)) ? 0 : 1;
        steps.Add(new("槽位提取", missing > 0 ? "warning" : "success", missing > 0 ? $"缺失 {missing} 项" : "槽位完整"));

        await tools.InvokeAsync(Read(traceId, "list_user_orders", userId, order, scenario, "INTENT_READY"), ct);
        var orderTool = await tools.InvokeAsync(Read(traceId, "get_order_detail", userId, order, scenario, "ORDER_CONFIRMED"), ct);
        steps.Add(new("订单查询", orderTool.Allowed ? "success" : "error", $"status={order.Status}, on_site={order.UserOnSite}, source={production.Mode}"));

        var matches = retrieval.Retrieve(order, policy, analyzed.Reason);
        await tools.InvokeAsync(Read(traceId, "get_policy_snapshot", userId, order, scenario, "ORDER_CONFIRMED"), ct);
        steps.Add(new("政策检索", "success", $"{matches[0].PolicyId} · score={matches[0].Score:0.00}"));

        var decision = rules.Evaluate(order, policy, scenario, signals);
        steps.Add(new("规则校验", decision.NeedsEvidence ? "warning" : "success", decision.RuleCode));
        steps.Add(new("风险判断", "success", $"{decision.RiskLevel} · {decision.RiskScore}"));

        var requiredTools = JsonSerializer.Deserialize<List<string>>(scenario.RequiredToolsJson) ?? [];
        var executed = new List<string>();
        string? confirmationToken = null;
        var writeToolName = scenario.ScenarioId == "I" ? "submit_order_change" : "submit_cancellation";
        var deferWriteToFunctionApproval = decision.NeedsUserConfirm && !request.ConfirmWrite;

        if (decision.NeedsUserConfirm)
        {
            confirmationToken = await confirmationStore.IssueAsync(
                scenario.CaseId, order.OrderId, order.Version,
                writeToolName,
                TimeSpan.FromMinutes(10), ct);
        }

        foreach (var toolName in requiredTools.Distinct())
        {
            var access = ToolGatewayWrite(toolName) ? ToolAccess.Write : ToolAccess.Read;
            var args = new Dictionary<string, object?>
            {
                ["refund"] = decision.RefundAmount,
                ["fee"] = decision.FeeAmount,
                ["summary"] = decision.Conclusion,
                ["reason"] = analyzed.Reason
            };

            string? token = null;
            string? idem = null;
            int? version = null;
            if (toolName is "submit_cancellation" or "submit_order_change" or "accept_supplier_offer" or "reserve_mock_alternative")
            {
                // Defer confirm-required writes to official FunctionApproval when not yet confirmed.
                if (deferWriteToFunctionApproval) continue;
                if (!(request.ConfirmWrite && confirmationToken is not null)) continue;
                token = request.ConfirmationToken ?? confirmationToken;
                idem = request.IdempotencyKey ?? $"idem-{scenario.ScenarioId}-{order.OrderId}-{toolName}";
                version = order.Version;
            }

            if (toolName is "submit_evidence_metadata" or "extract_evidence_fields" or "create_exception_review")
            {
                if (!signals.HasEvidence) continue;
            }
            if (toolName is "create_supplier_case" or "get_supplier_case" or "accept_supplier_offer")
            {
                if (decision.Action is "RequestInformation" or "RequestEvidence") continue;
            }

            var toolState = ToolStates.GetValueOrDefault(toolName, "DECISION_READY");
            if (scenario.ScenarioId == "H" && toolName == "create_human_handoff") toolState = "WAITING_EXTERNAL";
            if (scenario.ScenarioId == "K" && toolName == "create_human_handoff") toolState = "DECISION_READY";
            if ((scenario.ScenarioId is "E" or "L" or "D") && toolName == "create_human_handoff") toolState = "OPTION_PRESENTED";

            var result = await tools.InvokeAsync(new ToolCall(
                traceId, toolName, access, userId, order.OrderId, scenario.CaseId, decision.RiskLevel,
                toolState, args, token, idem, version), ct);
            if (result.Allowed) executed.Add(toolName);
        }

        await tools.InvokeAsync(new ToolCall(traceId, "calculate_refund_quote", ToolAccess.Read, userId, order.OrderId, scenario.CaseId, decision.RiskLevel, "DECISION_READY",
            new Dictionary<string, object?> { ["refund"] = decision.RefundAmount, ["fee"] = decision.FeeAmount }), ct);
        await tools.InvokeAsync(new ToolCall(traceId, "validate_action_permission", ToolAccess.Read, userId, order.OrderId, scenario.CaseId, decision.RiskLevel, "DECISION_READY",
            new Dictionary<string, object?> { ["action"] = decision.Action }), ct);

        if (decision.Action is "HumanHandoff" or "Recovery" or "FinanceReview" or "ServiceDispute" or "SpecialReview")
        {
            var handoff = await tools.InvokeAsync(new ToolCall(traceId, "create_human_handoff", ToolAccess.Write, userId, order.OrderId, scenario.CaseId, decision.RiskLevel, scenario.ScenarioId switch { "H" => "WAITING_EXTERNAL", "K" => "DECISION_READY", _ => "OPTION_PRESENTED" },
                new Dictionary<string, object?> { ["summary"] = decision.Conclusion }, IdempotencyKey: $"ho-{scenario.ScenarioId}-{runId}"), ct);
            if (handoff.Allowed) executed.Add("create_human_handoff");
        }
        if (decision.Action == "NegotiateWithHotel")
        {
            await tools.InvokeAsync(Read(traceId, "build_supplier_case_draft", userId, order, scenario, "FACTS_REQUIRED"), ct);
            await tools.InvokeAsync(new ToolCall(traceId, "create_supplier_case", ToolAccess.Write, userId, order.OrderId, scenario.CaseId, decision.RiskLevel, "CONFIRMATION_REQUIRED",
                new Dictionary<string, object?>(), IdempotencyKey: $"sup-{scenario.ScenarioId}-{runId}"), ct);
            executed.Add("create_supplier_case");
        }
        if (decision.Action == "ExplainProgress")
        {
            await tools.InvokeAsync(Read(traceId, "get_refund_status", userId, order, scenario, "TRACKING_REFUND"), ct);
            await tools.InvokeAsync(new ToolCall(traceId, "schedule_deadline_action", ToolAccess.Write, userId, order.OrderId, scenario.CaseId, decision.RiskLevel, "TRACKING_REFUND",
                new Dictionary<string, object?>(), IdempotencyKey: $"sch-{scenario.ScenarioId}-{runId}"), ct);
        }
        if ((decision.Action is "Recovery" or "HumanHandoff") && scenario.ScenarioId is "D" or "E")
        {
            await tools.InvokeAsync(Read(traceId, "verify_fulfillment_issue", userId, order, scenario, "DECISION_READY"), ct);
            await tools.InvokeAsync(Read(traceId, "get_alternative_hotels", userId, order, scenario, "DECISION_READY"), ct);
        }
        if (signals.HasEvidence && scenario.ScenarioId is "G" or "F" or "H")
        {
            await tools.InvokeAsync(new ToolCall(traceId, "submit_evidence_metadata", ToolAccess.Write, userId, order.OrderId, scenario.CaseId, decision.RiskLevel, "FACTS_REQUIRED",
                new Dictionary<string, object?> { ["evidence_type"] = "flight_cancel" }, IdempotencyKey: $"ev-{runId}"), ct);
        }

        steps.Add(new("处理动作", "active", decision.Action));

        TicketDto? ticket = null;
        if (decision.Action is "HumanHandoff" or "NegotiateWithHotel" or "Recovery" or "FinanceReview" or "SpecialReview" or "ServiceDispute")
        {
            ticket = new TicketDto(
                $"TKT-{scenario.ScenarioId}-{DateTime.UtcNow:HHmmss}",
                decision.RiskLevel == RiskLevel.L3 ? "P1" : "P2",
                decision.RiskLevel == RiskLevel.L3 ? "urgent" : "specialist",
                decision.PlanTitle,
                [
                    $"订单 {order.OrderId} / {order.HotelName}",
                    $"政策 {policy.PolicyId}",
                    $"结论 {decision.Conclusion}",
                    $"风险 {decision.RiskLevel}/{decision.RiskScore}"
                ],
                BuildTicketLifecycle(decision.Action, decision.RiskLevel, decision.CaseStatus));
        }

        HitlStateDto? hitl = null;
        if (decision.NeedsUserConfirm)
        {
            hitl = new HitlStateDto(
                true,
                writeToolName,
                confirmationToken,
                deferWriteToFunctionApproval
                    ? "FunctionApproval (ToolApprovalRequestContent) + confirmation_token + version + idempotency"
                    : "confirmation_token + expected_order_version + idempotency_key");
        }

        var suggestedReply = BuildReply(decision, order);
        var ambient = new Dictionary<string, object?>
        {
            ["refund"] = decision.RefundAmount,
            ["fee"] = decision.FeeAmount,
            ["summary"] = decision.Conclusion,
            ["reason"] = analyzed.Reason,
            ["action"] = decision.Action
        };

        // Drive ChatClientAgent: dialogue + official FunctionApproval for confirm-required writes.
        var plannedForAgent = new List<string>();
        if (deferWriteToFunctionApproval)
            plannedForAgent.Add(writeToolName);
        else if (executed.Count > 0)
            plannedForAgent.AddRange(executed.Take(2));

        var agentTurn = await conversation.RunTurnAsync(new AgentTurnRequest(
            request.Message,
            traceId,
            userId,
            order.OrderId,
            scenario.CaseId,
            scenario.ScenarioId,
            decision.RiskLevel,
            decision.ConversationState,
            plannedForAgent,
            suggestedReply,
            deferWriteToFunctionApproval,
            deferWriteToFunctionApproval ? writeToolName : null,
            ambient,
            confirmationToken,
            request.IdempotencyKey ?? (deferWriteToFunctionApproval ? $"idem-{scenario.ScenarioId}-{order.OrderId}-{writeToolName}" : null),
            deferWriteToFunctionApproval ? order.Version : null), ct);

        steps.Add(new(
            "Agent 驱动",
            agentTurn.AgentDriven ? "success" : "warning",
            agentTurn.HasPendingApprovals
                ? $"FunctionApproval 待批 ×{agentTurn.PendingApprovals.Count}"
                : $"provider={agentHost.ProviderName}, tools={string.Join(',', agentTurn.ToolsInvoked)}"));

        foreach (var t in agentTurn.ToolsInvoked)
        {
            if (!executed.Contains(t)) executed.Add(t);
        }

        var pendingApprovals = agentTurn.PendingApprovals
            .Select(p => new PendingApprovalDto(p.RequestId, p.CallId, p.ToolName, p.Arguments, p.Description))
            .ToList();

        var refundCase = new RefundCase
        {
            CaseId = scenario.CaseId,
            OrderId = order.OrderId,
            UserId = userId,
            ScenarioId = scenario.ScenarioId,
            Status = decision.CaseStatus,
            RiskLevel = decision.RiskLevel,
            Intent = analyzed.Intent,
            RecommendedAction = decision.Action,
            QuoteRefundAmount = decision.RefundAmount,
            QuoteFeeAmount = decision.FeeAmount,
            ConversationState = decision.ConversationState,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await store.UpsertCaseAsync(refundCase, ct);
        await store.AppendEventAsync(scenario.CaseId, "agent_decision", new
        {
            decision.Action,
            decision.RuleCode,
            executed,
            confirmationToken,
            agentSessionId = agentTurn.SessionId,
            pendingApprovals = pendingApprovals.Select(p => p.ToolName)
        }, ct);

        var run = new WorkflowRun
        {
            RunId = runId,
            CaseId = scenario.CaseId,
            ScenarioId = scenario.ScenarioId,
            Status = agentTurn.HasPendingApprovals ? "WAITING_APPROVAL" : "COMPLETED",
            TraceJson = JsonSerializer.Serialize(steps),
            ToolSequenceJson = JsonSerializer.Serialize(executed.Distinct()),
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        };
        await store.SaveWorkflowRunAsync(run, ct);
        await sessionStore.SetAsync(
            $"session:{userId}:{order.OrderId}",
            JsonSerializer.Serialize(new { scenario.CaseId, runId, confirmationToken, agentSessionId = agentTurn.SessionId }),
            TimeSpan.FromHours(6), ct);

        logger.LogInformation(
            "Scenario {Scenario} action {Action} aiProvider={Provider} agentDriven={Driven} pendingApprovals={Pending} production={Mode}",
            scenario.ScenarioId, decision.Action, agentHost.ProviderName, agentTurn.AgentDriven,
            pendingApprovals.Count, production.Mode);

        var dto = new AgentDecisionDto(
            traceId, runId, scenario.CaseId, scenario.ScenarioId,
            analyzed.Intent, analyzed.Confidence, decision.RiskLevel, decision.RiskScore,
            decision.Action, decision.Conclusion, decision.PlanTitle, decision.PlanCopy,
            decision.RefundAmount, decision.FeeAmount,
            agentTurn.Reply,
            decision.ConversationState, decision.CaseStatus,
            steps, analyzed.Slots, matches, executed.Distinct().ToList(), ticket,
            new HotelOrderDto(order.OrderId, order.HotelName, order.CheckIn, order.CheckOut, order.PaidAmount, order.Currency,
                order.Status, order.UserOnSite, order.PolicyId, order.Version, order.RoomType, order.RoomCount),
            false, Array.Empty<string>(), hitl, agentHost.ProviderName,
            agentTurn.SessionId, agentTurn.AgentDriven, agentTurn.HasPendingApprovals, pendingApprovals, production.Mode);
        var verification = verifier.VerifyDecision(dto);
        return dto with { VerificationPassed = verification.Passed, VerificationViolations = verification.Violations };
    }

    public async Task<AgentDecisionDto> RespondToApprovalAsync(FunctionApprovalRequest request, CancellationToken ct = default)
    {
        var snapshot = await agentSessionStore.GetAsync(request.SessionId, ct)
                       ?? throw new InvalidOperationException("agent session not found or expired");

        var agentTurn = await conversation.RespondToApprovalAsync(
            new ApprovalResponseRequest(request.SessionId, request.RequestId, request.Approved, request.Reason), ct);

        var order = await store.GetOrderAsync(snapshot.OrderId, ct)
                    ?? throw new InvalidOperationException($"missing order {snapshot.OrderId}");
        var risk = Enum.TryParse<RiskLevel>(snapshot.RiskLevel, out var rl) ? rl : RiskLevel.L1;
        var pending = agentTurn.PendingApprovals
            .Select(p => new PendingApprovalDto(p.RequestId, p.CallId, p.ToolName, p.Arguments, p.Description))
            .ToList();

        await store.AppendEventAsync(snapshot.CaseId, "function_approval", new
        {
            request.RequestId,
            request.Approved,
            request.Reason,
            tools = agentTurn.ToolsInvoked
        }, ct);

        var steps = new List<DecisionStepDto>
        {
            new("FunctionApproval", request.Approved ? "success" : "warning",
                request.Approved ? $"已批准 {request.RequestId}" : $"已拒绝 {request.RequestId}"),
            new("Agent 续跑", agentTurn.AgentDriven ? "success" : "warning",
                agentTurn.HasPendingApprovals
                    ? $"仍有待批 ×{pending.Count}"
                    : string.Join(',', agentTurn.ToolsInvoked))
        };

        var dto = new AgentDecisionDto(
            snapshot.TraceId,
            $"apr_{Guid.NewGuid():N}"[..16],
            snapshot.CaseId,
            string.IsNullOrWhiteSpace(snapshot.ScenarioId) ? "A" : snapshot.ScenarioId,
            "function_approval",
            1.0,
            risk,
            risk == RiskLevel.L3 ? 90 : 40,
            request.Approved ? "WriteApproved" : "WriteRejected",
            agentTurn.Reply,
            request.Approved ? "写操作已批准" : "写操作已拒绝",
            agentTurn.Reply,
            null, null,
            agentTurn.Reply,
            snapshot.ConversationState,
            request.Approved ? "REFUND_INITIATED" : "AWAITING_USER",
            steps,
            new Dictionary<string, string>(),
            [],
            agentTurn.ToolsInvoked.ToList(),
            null,
            new HotelOrderDto(order.OrderId, order.HotelName, order.CheckIn, order.CheckOut, order.PaidAmount, order.Currency,
                order.Status, order.UserOnSite, order.PolicyId, order.Version, order.RoomType, order.RoomCount),
            true, Array.Empty<string>(),
            pending.Count > 0
                ? new HitlStateDto(true, pending[0].ToolName, snapshot.ConfirmationToken,
                    "FunctionApproval (ToolApprovalRequestContent)")
                : null,
            agentHost.ProviderName,
            agentTurn.SessionId,
            agentTurn.AgentDriven,
            agentTurn.HasPendingApprovals,
            pending,
            production.Mode);

        return dto;
    }

    private static IReadOnlyList<TicketLifecycleStepDto> BuildTicketLifecycle(string action, RiskLevel risk, string caseStatus)
    {
        var queue = risk == RiskLevel.L3 ? "紧急专席" : "专项队列";
        return
        [
            new("受理建单", "done", $"已创建 {queue} 工单上下文"),
            new("事实汇总", "done", "订单 / 政策 / 风险已写入工单摘要"),
            new("专席认领", caseStatus is "ESCALATED" or "WAITING_EXTERNAL" or "SPECIAL_REVIEW" ? "active" : "pending",
                action is "NegotiateWithHotel" ? "等待供应商回执" : "等待专员接手"),
            new("用户可见更新", "pending", "公开进度文案待专席确认后同步"),
            new("结案回写", "pending", "恢复会话与审计 Trace 待闭环")
        ];
    }

    private static readonly Dictionary<string, string> ToolStates = new()
    {
        ["list_user_orders"] = "INTENT_READY",
        ["get_order_detail"] = "ORDER_CONFIRMED",
        ["get_policy_snapshot"] = "ORDER_CONFIRMED",
        ["list_after_sale_events"] = "FACTS_REQUIRED",
        ["calculate_refund_quote"] = "DECISION_READY",
        ["validate_action_permission"] = "DECISION_READY",
        ["submit_cancellation"] = "CONFIRMATION_REQUIRED",
        ["get_refund_status"] = "TRACKING_REFUND",
        ["get_payment_events"] = "TRACKING_REFUND",
        ["schedule_deadline_action"] = "TRACKING_REFUND",
        ["create_payment_investigation"] = "WAITING_EXTERNAL",
        ["verify_fulfillment_issue"] = "ORDER_CONFIRMED",
        ["get_alternative_hotels"] = "DECISION_READY",
        ["get_guarantee_quote"] = "DECISION_READY",
        ["create_human_handoff"] = "OPTION_PRESENTED",
        ["build_supplier_case_draft"] = "FACTS_REQUIRED",
        ["create_supplier_case"] = "CONFIRMATION_REQUIRED",
        ["submit_evidence_metadata"] = "FACTS_REQUIRED",
        ["extract_evidence_fields"] = "FACTS_REQUIRED",
        ["create_exception_review"] = "DECISION_READY",
        ["create_service_dispute_case"] = "DECISION_READY",
        ["get_change_quote"] = "DECISION_READY",
        ["submit_order_change"] = "CONFIRMATION_REQUIRED",
        ["create_finance_case"] = "DECISION_READY",
        ["get_responsibility_chain"] = "DECISION_READY",
        ["get_group_order_breakdown"] = "FACTS_REQUIRED",
        ["get_partial_cancel_quote"] = "DECISION_READY",
        ["get_supplier_case"] = "WAITING_EXTERNAL",
        ["accept_supplier_offer"] = "OPTION_PRESENTED",
        ["get_handoff_status"] = "ESCALATED",
        ["confirm_recovery_outcome"] = "ESCALATED",
        ["reserve_mock_alternative"] = "OPTION_PRESENTED",
    };

    private static bool NeedsEvidence(string scenarioId, AgentSignals signals) =>
        scenarioId is "G" || (scenarioId is "F" && !signals.HasNegotiationReason);

    private static bool ToolGatewayWrite(string name) =>
        name.StartsWith("submit_") || name.StartsWith("create_") || name.StartsWith("accept_") ||
        name.StartsWith("reserve_") || name.StartsWith("confirm_") || name.StartsWith("schedule_");

    private static ToolCall Read(string traceId, string tool, string userId, HotelOrder order, ScenarioFixture scenario, string state) =>
        new(traceId, tool, ToolAccess.Read, userId, order.OrderId, scenario.CaseId, scenario.RiskLevel, state, new Dictionary<string, object?>());

    private static string BuildReply(RuleDecision d, HotelOrder order) => d.Action switch
    {
        "RequestEvidence" => $"已核对订单 {order.OrderId}（{order.HotelName}）。{d.Conclusion}。请先上传相关证明。",
        "RequestInformation" => $"{d.Conclusion}。请补充无法入住的具体原因后，我再发起协商。",
        "ConfirmCancel" => $"{d.Conclusion}。预计退回 {order.Currency} {d.RefundAmount:0.##}，费用 {d.FeeAmount:0.##}。确认后提交。",
        "Clarify" => d.PlanCopy,
        _ => $"{d.Conclusion}。{d.PlanCopy}"
    };
}

public sealed class EvalRunner(IRefundDataStore store, IAgentOrchestrator orchestrator) : IEvalRunner
{
    private readonly ScenarioRouter _router = new();

    public IReadOnlyList<EvalCaseDto> ListCases()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../eval/agent-eval-cases.json"));
        if (!File.Exists(path))
            path = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../eval/agent-eval-cases.json"));
        var root = Environment.GetEnvironmentVariable("STAYOTA_AGENT_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
            path = Path.Combine(root, "eval/agent-eval-cases.json");

        if (!File.Exists(path))
        {
            return ScenarioCodes.All.SelectMany(s => Enumerable.Range(1, 3).Select(i =>
                new EvalCaseDto($"EVAL-{s}-{i:00}", $"scenario {s} sample {i}", s, "L1"))).ToList();
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("cases").EnumerateArray().Select(c =>
            new EvalCaseDto(
                c.GetProperty("id").GetString()!,
                c.GetProperty("message").GetString()!,
                c.GetProperty("expected_scenario").GetString()!,
                c.GetProperty("risk_level").GetString()!)).ToList();
    }

    public async Task<IReadOnlyList<EvalResultDto>> RunAllAsync(CancellationToken ct = default)
    {
        await store.EnsureSeededAsync(ct);
        var results = new List<EvalResultDto>();
        foreach (var c in ListCases())
        {
            var routed = _router.Route(c.Message, null);
            var passed = routed == c.ExpectedScenario;
            string? detail = null;
            if (passed)
            {
                try
                {
                    var decision = await orchestrator.HandleAsync(new AgentMessageRequest(c.Message, c.ExpectedScenario, ResetDemo: false), ct);
                    detail = $"{decision.Action}/{decision.CaseStatus}";
                    passed = decision.ScenarioId == c.ExpectedScenario;
                }
                catch (Exception ex)
                {
                    passed = false;
                    detail = ex.Message;
                }
            }
            else detail = $"routed={routed}";
            results.Add(new EvalResultDto(c.Id, c.Message, c.ExpectedScenario, routed, passed, detail));
        }
        return results;
    }
}
