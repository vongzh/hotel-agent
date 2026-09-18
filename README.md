# StayOTA Agent（酒店 OTA Agent）

本仓库是**独立的酒店 OTA Agent**（StayOTA Agent）：面向酒店订单场景的受控 AI Agent，覆盖意图理解、政策检索、规则/风险、33 Tool 写门禁、A–L Workflow 与离线 Eval。默认演示场景为退款处理，可扩展为更多 OTA 能力。

技术栈：**.NET 10 + Microsoft.Extensions.AI + Microsoft Agent Framework + Vue3（Vben 风格）+ PostgreSQL + Redis**

## 架构要点

| 层 | 实现 |
| --- | --- |
| 模块布局 | `StayOta.Agent.Abstractions` + `StayOta.Agent` + `Plugins.Refund` + `StayOta.Agent.Host` |
| 模型接入 | `IChatClient`（默认 `DeterministicRefundChatClient`，可换成 Azure OpenAI / Foundry） |
| Agent | `ChatClientAgent`（`Microsoft.Agents.AI`） |
| A–L 编排 | `WorkflowBuilder` + `InProcessExecution`（`Microsoft.Agents.AI.Workflows`） |
| 33 Tool | `AIFunctionFactory` + `ApprovalRequiredAIFunction`（确认类写操作） |
| FunctionApproval | `ToolApprovalRequestContent` → `POST /api/agent/approvals` |
| MCP | `MapMcp("/mcp")` + `RefundMcpTools`；`Production:Mode=Mcp` 可拉外部工具 |
| 生产直连 | `Production:Mode=Mock\|Http\|Mcp`（Http 走 BaseUrl 订单/政策 API） |
| 领域门禁 | `ToolGateway`（确认令牌 / 版本 / 幂等 / 审计） |
| 规则 / 风险 / Eval | Domain + Verifier |
| PG 隔离 | `AgentStorage:Schema=agent_refund`（默认同库 schema 隔离） |

## 已覆盖能力

- A–L 十二场景 + 低置信度 / 服务异常边界态
- 33 Tool 契约注册与 Mock 执行（权限门：确认令牌 / 版本 / 幂等 / 审计）
- 意图 · 槽位 · 政策检索 Top3 · 规则引擎 · 风险分层
- Session（Redis）/ Case Event / Workflow Trace（PostgreSQL）
- 36 条离线 Eval（`POST /api/eval/run`）
- A–L Agent Framework Workflow（`POST /api/workflows/run-all`）
- Tool `allowed_conversation_states` 白名单门禁
- Verifier 决策/工作流断言
- 前端三页：Agent 设计 / 智能处理台 / 运营看板
- ChatClientAgent 真正驱动对话（Deterministic / OpenAI / Ollama）
- 官方 FunctionApproval 流（待批 UI + `/api/agent/approvals`）
- MCP 暴露 `/mcp` + 生产直连 `Mock|Http|Mcp`

## 启动

```bash
# 依赖：Postgres + Redis（可用 docker compose up -d）
export PATH="$HOME/.dotnet:$PATH"
cd backend
dotnet run --project src/Hosts/StayOta.Agent.Host --urls http://127.0.0.1:5088

cd ../frontend
npm install && npm run dev
```

- API Swagger: http://127.0.0.1:5088/swagger
- MCP: http://127.0.0.1:5088/mcp
- 前端: http://127.0.0.1:5173
- Health 会返回 `aiProvider` / `agent` / `productionMode` / `mcpEndpoint` / `pgSchema` / `stack`

## 测试

```bash
cd backend && dotnet test StayOta.Agent.slnx
curl -X POST http://127.0.0.1:5088/api/eval/run
curl -X POST http://127.0.0.1:5088/api/workflows/run-all
```

## Production 配置

```bash
export ASPNETCORE_ENVIRONMENT=Production
export ConnectionStrings__Postgres='...'
export ConnectionStrings__Redis='...'
export Hosting__ApiKey='...'
export Production__Mode=Http
export Production__BaseUrl='https://orders.internal/'
```

- `DemoEnabled=false`：禁止启动删库、`ResetDemo`、Eval/Workflow 演示端、开放确认签发
- Http/Mcp：**不**静默回退 Mock；AI 失败不静默降级 Deterministic（除非显式允许）

与主站订单/鉴权等系统对接时，可参考可选说明 [`docs/STAYOTA-INTEGRATION.md`](./docs/STAYOTA-INTEGRATION.md)（非本仓运行前置依赖）。

## AI 提供商

默认 `Deterministic`（离线演示）。可在 `appsettings.json` 或环境变量切换：

```bash
# OpenAI / 兼容网关
export AI_PROVIDER=OpenAI
export OPENAI_API_KEY=sk-...
# optional: OPENAI_ENDPOINT=https://...  OPENAI_MODEL=gpt-4o-mini

# Ollama 本地
export AI_PROVIDER=Ollama
export OLLAMA_ENDPOINT=http://127.0.0.1:11434
export OLLAMA_MODEL=llama3.2
```

Demo 下配置错误会回退 Deterministic；正式态（`AllowDeterministicFallback=false`）则启动失败，避免静默降级。
