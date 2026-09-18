using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Plugins.Refund.Services;
using StayOta.Agent.Abstractions.Domain;
using StayOta.Agent.Abstractions.Domain.Entities;
using Xunit;

namespace StayOta.Agent.Tests;

public class RulesEngineTests
{
    private readonly RulesEngine _rules = new();

    private static ScenarioFixture Sc(string id, RiskLevel risk = RiskLevel.L1) => new()
    {
        ScenarioId = id, RiskLevel = risk, OrderId = $"ORD-{id}-001", CaseId = $"CASE-{id}-001", Title = id
    };

    [Theory]
    [InlineData("A", "ConfirmCancel")]
    [InlineData("B", "ConfirmCancel")]
    [InlineData("C", "ExplainProgress")]
    [InlineData("D", "Recovery")]
    [InlineData("E", "HumanHandoff")]
    [InlineData("I", "ChangeOrder")]
    [InlineData("J", "FinanceReview")]
    [InlineData("K", "HumanHandoff")]
    [InlineData("L", "HumanHandoff")]
    public void ScenarioActions(string id, string action)
    {
        var risk = id is "D" or "E" or "H" or "J" or "K" or "L" ? RiskLevel.L3 : id is "F" or "G" ? RiskLevel.L2 : RiskLevel.L1;
        var r = _rules.Evaluate(new HotelOrder { PaidAmount = 1000, RoomCount = 8 }, new PolicySnapshot { RuleCode = "P" }, Sc(id, risk),
            new AgentSignals(false, false, false, false, false));
        Assert.Equal(action, r.Action);
    }

    [Fact]
    public void G_RequiresEvidence()
    {
        var r = _rules.Evaluate(new HotelOrder { PaidAmount = 520 }, new PolicySnapshot { RuleCode = "P" }, Sc("G", RiskLevel.L2),
            new AgentSignals(false, false, false, false, false));
        Assert.Equal("RequestEvidence", r.Action);
    }
}

public class PolicyRetrievalTests
{
    private readonly PolicyRetrieval _retrieval = new();

    [Fact]
    public void FlightCancel_RanksForceMajeurePolicy()
    {
        var matches = _retrieval.Retrieve(
            new HotelOrder { Status = "CONFIRMED", PolicyId = "POL-G" },
            new PolicySnapshot { PolicyId = "POL-G", Title = "特殊原因", Summary = "航班取消需材料" },
            "flight_cancelled");
        Assert.True(matches.Count >= 3);
        Assert.Contains(matches, m => m.PolicyId.Contains("005") || m.Summary.Contains("不可抗力") || m.Score >= 0.5);
        Assert.True(matches[0].Score >= matches[1].Score);
    }

    [Fact]
    public void FreeCancel_PrefersFreeCancellationPolicy()
    {
        var matches = _retrieval.Retrieve(
            new HotelOrder { Status = "CONFIRMED", PolicyId = "POL-A" },
            new PolicySnapshot { PolicyId = "POL-A", Title = "免费取消", Summary = "截止前免费" },
            "free_cancel");
        Assert.Contains(matches, m => m.Title.Contains("免费") || m.PolicyId == "POL-A");
    }
}

public class ScenarioRouterTests
{
    private readonly ScenarioRouter _router = new();

    [Theory]
    [InlineData("帮我把明天去杭州的酒店免费取消。", "A")]
    [InlineData("今天不去了，现在取消要扣多少？", "B")]
    [InlineData("退款已经提交三天了，怎么还没有到账？", "C")]
    [InlineData("酒店刚通知没房，还要求临时加价换房。", "D")]
    [InlineData("我已经到前台了，但是酒店说没有房间。", "E")]
    [InlineData("酒店说不能退，可以帮我争取一下吗？", "F")]
    [InlineData("航班取消了，可以凭证明申请退款吗？", "G")]
    [InlineData("房间和图片不一样，而且卫生很脏。", "H")]
    [InlineData("日期订错了一天，可以帮我改日期吗？", "I")]
    [InlineData("同一笔房费扣了两次，像是重复扣款。", "J")]
    [InlineData("海外酒店让我找代理，代理又让我找平台，到底谁负责？", "K")]
    [InlineData("公司订了八间房，只想部分取消并处理发票。", "L")]
    public void RoutesExpected(string message, string expected) =>
        Assert.Equal(expected, _router.Route(message, null));
}

public class VerifierTests
{
    private readonly Verifier _verifier = new();

    [Fact]
    public void RejectsL3AutoRefund()
    {
        var decision = new AgentDecisionDto(
            "t", "r", "c", "E", "intent", 0.9, RiskLevel.L3, 90, "ConfirmCancel",
            "x", "y", "z", 100, 0, "reply", "ESCALATED", "X",
            [new("意图识别", "success", "a"), new("槽位提取", "success", "b"), new("订单查询", "success", "c"), new("政策检索", "success", "d"), new("规则校验", "success", "e"), new("风险判断", "success", "f"), new("处理动作", "active", "g")],
            new Dictionary<string, string>(),
            [new("P", "t", 0.9, "s")],
            [], null,
            new HotelOrderDto("o", "h", DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today), 100, "CNY", "CONFIRMED", true, "P", 1, "room", 1),
            false, []);
        var result = _verifier.VerifyDecision(decision);
        Assert.False(result.Passed);
        Assert.Contains(result.Violations, v => v.Contains("L3"));
    }

    [Fact]
    public void WorkflowSubsequence()
    {
        var run = new WorkflowRunResultDto("r", "A", "R", "c", "S", "REFUND_INITIATED",
            ["list_user_orders", "get_order_detail", "submit_cancellation"],
            [new WorkflowStepDto(1, "TOOL", "TOOL", "A", "B", "list_user_orders", null)],
            new WorkflowAssertionDto(true, true, true, true), true);
        var result = _verifier.VerifyWorkflow(run, ["list_user_orders", "get_order_detail", "submit_cancellation"], "REFUND_INITIATED");
        Assert.True(result.Passed);
    }
}
