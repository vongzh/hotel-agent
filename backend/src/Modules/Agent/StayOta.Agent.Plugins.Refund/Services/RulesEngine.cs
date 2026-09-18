using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Domain;
using StayOta.Agent.Abstractions.Domain.Entities;

namespace StayOta.Agent.Plugins.Refund.Services;

public sealed class RulesEngine : IRulesEngine
{
    public RuleDecision Evaluate(HotelOrder order, PolicySnapshot policy, ScenarioFixture scenario, AgentSignals signals)
    {
        if (signals.ServiceError)
        {
            return new("Clarify", RiskLevel.L2, 40, 0, 0,
                "订单服务暂时不可用", "请稍后重新查询", "模拟订单查询失败，请稍后重试。",
                "SERVICE_ERROR", false, false, "START", "OPEN");
        }

        if (signals.LowConfidence)
        {
            return new("Clarify", RiskLevel.L1, 35, 0, 0,
                "当前意图置信度不足", "先澄清诉求", "请补充你希望取消、查进度还是协商退款。",
                "LOW_CONFIDENCE", false, false, "INTENT_READY", "OPEN");
        }

        return scenario.ScenarioId switch
        {
            "A" => Dec("ConfirmCancel", RiskLevel.L1, 18, order.PaidAmount, 0,
                "可以免费取消", "免费取消并原路退款",
                $"确认后立即取消，预计退回 {order.Currency} {order.PaidAmount:0.##}。",
                policy.RuleCode, true, false, "CONFIRMATION_REQUIRED", "WAITING_USER_CONFIRM"),
            "B" => Dec("ConfirmCancel", RiskLevel.L1, 28, order.PaidAmount / 2, order.PaidAmount / 2,
                "可以取消，但会扣除部分房费", "接受扣费并取消",
                $"预计退回 {order.PaidAmount / 2:0.##}，扣费 {order.PaidAmount / 2:0.##}。",
                policy.RuleCode, true, false, "CONFIRMATION_REQUIRED", "WAITING_USER_CONFIRM"),
            "C" => Dec("ExplainProgress", RiskLevel.L1, 22, order.PaidAmount, 0,
                "退款已发起，资金仍在渠道处理中", "继续等待原路退款",
                "无需重复申请；超时未到账将自动创建支付调查。",
                "REFUND_TRACKING", false, false, "TRACKING_REFUND", "AWAITING_PAYMENT"),
            "D" => Dec("Recovery", RiskLevel.L3, 84, order.PaidAmount, 0,
                "入住前无房/加价，先安排替代住宿", "转紧急履约恢复",
                "额外费用需确认；已准备人工协同。",
                "PREARRIVAL_RECOVERY", false, false, "ESCALATED", "RECOVERY_IN_PROGRESS"),
            "E" => Dec("HumanHandoff", RiskLevel.L3, 92, order.PaidAmount, 0,
                "到店无房，优先今晚住宿", "立即转紧急专员",
                "退款与责任认定在安顿后继续。",
                "ONSITE_URGENT", false, false, "ESCALATED", "RECOVERED"),
            "F" when !signals.HasNegotiationReason && !signals.HasEvidence =>
                Dec("RequestInformation", RiskLevel.L2, 55, 0, 0,
                    "不可取消订单，需先补充无法入住原因", "补充信息后发起协商",
                    "信息完整后生成酒店协商草案。",
                    "NON_REFUNDABLE", false, true, "FACTS_REQUIRED", "WAITING_EVIDENCE"),
            "F" => Dec("NegotiateWithHotel", RiskLevel.L2, 62, 0, 0,
                "不能直接退款，可发起例外协商", "提交供应商协商",
                "不承诺一定成功，结果以酒店回复为准。",
                "SUPPLIER_NEGOTIATION", true, false, "CONFIRMATION_REQUIRED", "WAITING_SUPPLIER"),
            "G" when !signals.HasEvidence =>
                Dec("RequestEvidence", RiskLevel.L2, 58, 0, 0,
                    "特殊原因需先上传证明", "补充航班/疾病等证明",
                    "材料可触发审核，但不保证全额退款。",
                    "SPECIAL_EXCEPTION", false, true, "FACTS_REQUIRED", "WAITING_EVIDENCE"),
            "G" => Dec("SpecialReview", RiskLevel.L2, 64, 0, 0,
                "材料已接收，进入特殊审核", "创建例外审核工单",
                "审核完成前不自动全退。",
                "SPECIAL_EXCEPTION_REVIEW", false, false, "ESCALATED", "MANUAL_REVIEW"),
            "H" => Dec("ServiceDispute", RiskLevel.L3, 78, 0, 0,
                "先处理当前入住问题，再核实赔付", "创建服务争议工单",
                "审核完成前不承诺具体金额。",
                "SERVICE_DISPUTE", false, false, "WAITING_EXTERNAL", "MANUAL_REVIEW"),
            "I" => Dec("ChangeOrder", RiskLevel.L1, 30, 80, 0,
                "可以改期，需补差价", "确认改期报价",
                "改期补差 80，对比取消扣费 300。",
                "ORDER_CHANGE", true, false, "CONFIRMATION_REQUIRED", "WAITING_USER_CONFIRM"),
            "J" => Dec("FinanceReview", RiskLevel.L3, 76, 0, 0,
                "区分实扣与预授权，转财务核验", "创建财务工单",
                "预授权冻结不等于第二笔实扣。",
                "PAYMENT_ANOMALY", false, false, "WAITING_EXTERNAL", "MANUAL_REVIEW"),
            "K" => Dec("HumanHandoff", RiskLevel.L3, 80, 0, 0,
                "跨境责任链需专席跟进", "转跨境专席",
                "平台统一受理，执行方可能在海外供应商。",
                "CROSS_BORDER", false, false, "ESCALATED", "AWAITING_SPECIALIST"),
            "L" => Dec("HumanHandoff", RiskLevel.L3, 82, 3000, 300,
                "团体部分取消仅可预览，禁止自动写入", "提交团体专席确认",
                "写操作已阻断，待专席确认发票影响。",
                "CORPORATE_GROUP", false, false, "ESCALATED", "AWAITING_SPECIALIST"),
            _ => Dec("Clarify", RiskLevel.L1, 20, 0, 0, "暂无法判断", "澄清诉求", "请补充更多信息。",
                "UNKNOWN", false, false, "INTENT_READY", "OPEN")
        };
    }

    private static RuleDecision Dec(
        string action, RiskLevel risk, int score, decimal refund, decimal fee,
        string conclusion, string planTitle, string planCopy, string ruleCode,
        bool confirm, bool evidence, string state, string caseStatus) =>
        new(action, risk, score, refund, fee, conclusion, planTitle, planCopy, ruleCode, confirm, evidence, state, caseStatus);
}
