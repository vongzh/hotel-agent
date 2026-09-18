using Microsoft.Extensions.AI;
using StayOta.Agent.Ai;
using Xunit;

namespace StayOta.Agent.Tests;

public class ToolIntentPlannerTests
{
    private static readonly string[] Catalog =
    [
        "list_user_orders", "get_order_detail", "get_policy_snapshot", "calculate_refund_quote",
        "validate_action_permission", "get_refund_status", "get_payment_events", "schedule_deadline_action",
        "verify_fulfillment_issue", "get_alternative_hotels", "create_human_handoff",
        "build_supplier_case_draft", "get_change_quote", "create_finance_case",
        "get_responsibility_chain", "get_group_order_breakdown", "get_partial_cancel_quote",
        "submit_cancellation"
    ];

    [Fact]
    public void CancelMessage_SelectsQuoteTools()
    {
        var tools = ToolIntentPlanner.Select("帮我把明天去杭州的酒店免费取消。", "INTENT_READY", Catalog);
        Assert.Contains("get_order_detail", tools);
        Assert.Contains("calculate_refund_quote", tools);
        Assert.DoesNotContain("create_finance_case", tools);
    }

    [Fact]
    public void ProgressMessage_SelectsRefundTrackingTools()
    {
        var tools = ToolIntentPlanner.Select("退款已经提交三天了，怎么还没有到账？", "TRACKING_REFUND", Catalog);
        Assert.Contains("get_refund_status", tools);
        Assert.Contains("get_payment_events", tools);
    }

    [Fact]
    public void PreferredWriteTool_IsIncluded()
    {
        var tools = ToolIntentPlanner.Select("确认取消", "CONFIRMATION_REQUIRED", Catalog, "submit_cancellation");
        Assert.Contains("submit_cancellation", tools);
    }

    [Fact]
    public async Task AutonomousDeterministicClient_CallsPlannedFromMessage()
    {
        DeterministicRefundChatClient.PlannedTools.Value = [];
        DeterministicRefundChatClient.AllowAutonomousToolSelection.Value = true;
        DeterministicRefundChatClient.UserMessage.Value = "帮我取消酒店订单";
        DeterministicRefundChatClient.ConversationState.Value = "DECISION_READY";
        DeterministicRefundChatClient.FinalReply.Value = "ok";
        DeterministicRefundChatClient.PreferredWriteTool.Value = null;

        var client = new DeterministicRefundChatClient();
        var getOrder = AIFunctionFactory.Create(() => new { ok = true }, "get_order_detail", "order");
        var quote = AIFunctionFactory.Create(() => new { ok = true }, "calculate_refund_quote", "quote");
        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "帮我取消酒店订单")],
            new ChatOptions { Tools = [getOrder, quote] });

        var call = response.Messages.SelectMany(m => m.Contents)
            .OfType<FunctionCallContent>().FirstOrDefault();
        Assert.NotNull(call);
        Assert.Equal("get_order_detail", call!.Name);
    }
}
