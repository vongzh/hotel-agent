namespace StayOta.Agent.Abstractions.Domain;

public enum RiskLevel { L1 = 1, L2 = 2, L3 = 3 }

public enum ToolAccess { Read = 1, Write = 2 }

public enum AgentAction
{
    AutoRefund,
    ConfirmCancel,
    RequestEvidence,
    RequestInformation,
    NegotiateWithHotel,
    HumanHandoff,
    ExplainProgress,
    Clarify,
    ChangeOrder,
    FinanceReview,
    SpecialReview,
    ServiceDispute,
    Recovery
}

public static class ScenarioCodes
{
    public static readonly string[] All = ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L"];
}
