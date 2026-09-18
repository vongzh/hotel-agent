# StayOTA Agent 架构改造计划（MEAI + Agent Framework）

## 目标

减少手写 Orchestrator / FSM / Tool 调度引擎，把编排与模型接入迁到微软官方栈，领域规则与门禁保留自研。

## 能力矩阵（演示）

| 能力块 | 状态 |
| --- | --- |
| A–L 十二场景 + 边界态 | ✅ |
| 33 Tool 契约与执行 | ✅ AIFunction + ToolGateway |
| 意图/槽位/政策检索/规则/风险 | ✅ Domain |
| Tool 权限门 | ✅ ToolGateway |
| Session / Case / Trace | ✅ PG + Redis |
| 36 条离线 Eval | ✅ |
| A–L Workflow | ✅ `Microsoft.Agents.AI.Workflows` |
| ChatClientAgent | ✅ 默认确定性 Client |
| 真 LLM | 🔜 替换 `IChatClient` |
| 官方 Vben monorepo | 🔜 |

## 已落地改造

1. 引入 `Microsoft.Extensions.AI` / `Microsoft.Agents.AI` / `Microsoft.Agents.AI.Workflows`
2. `RefundAiToolCatalog`：`AIFunctionFactory` + `ApprovalRequiredAIFunction`
3. `ScenarioWorkflow`：`WorkflowBuilder` 动态边 + `InProcessExecution`
4. `RefundAgentHost`：`ChatClientAgent` + `UseFunctionInvocation`
5. `DeterministicRefundChatClient`：离线演示；可换 Azure OpenAI / Foundry

## 后续

- 注册真实 `IChatClient`（Azure OpenAI / Foundry）
- HITL 对齐 `FunctionApprovalRequestContent` 到前端确认流
- MCP 暴露外部业务 Tool
- 迁入官方 vue-vben-admin
