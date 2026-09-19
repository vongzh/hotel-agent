using StayOta.Agent.Abstractions.Domain;
using StayOta.Agent.Abstractions.Tools;
using Xunit;

namespace StayOta.Agent.Tests;

public class ToolPolicyTests
{
    [Fact]
    public void WriteAndConfirm_AreConsistent()
    {
        Assert.True(ToolPolicy.IsWrite("submit_cancellation"));
        Assert.True(ToolPolicy.RequiresConfirmation("submit_cancellation"));
        Assert.True(ToolPolicy.IsWrite("create_human_handoff"));
        Assert.False(ToolPolicy.RequiresConfirmation("create_human_handoff"));
        Assert.False(ToolPolicy.IsWrite("get_order_detail"));
        Assert.Equal(ToolAccess.Write, ToolPolicy.AccessOf("submit_order_change"));
        Assert.Equal(ToolAccess.Read, ToolPolicy.AccessOf("calculate_refund_quote"));
    }

    [Fact]
    public void StateFor_AppliesScenarioOverrides()
    {
        Assert.Equal("WAITING_EXTERNAL", ToolPolicy.StateFor("create_human_handoff", "H"));
        Assert.Equal("DECISION_READY", ToolPolicy.StateFor("create_human_handoff", "K"));
        Assert.Equal("OPTION_PRESENTED", ToolPolicy.StateFor("create_human_handoff", "E"));
        Assert.Equal("CONFIRMATION_REQUIRED", ToolPolicy.StateFor("submit_cancellation"));
    }
}
