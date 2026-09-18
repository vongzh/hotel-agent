export interface Scenario {
  scenarioId: string
  title: string
  group: string
  goal: string
  entryMessage: string
  riskLevel: string
  expectedRoute: string
  requiredTools: string[]
}

export interface DecisionStep {
  step: string
  status: string
  detail: string
  score?: number
}

export interface PolicyMatch {
  policyId: string
  title: string
  score: number
  summary: string
}

export interface TicketLifecycleStep {
  stage: string
  status: string
  detail: string
}

export interface Ticket {
  ticketId: string
  priority: string
  queue: string
  summary: string
  facts: string[]
  lifecycle?: TicketLifecycleStep[]
}

export interface HitlState {
  requiresConfirmation: boolean
  pendingAction?: string
  confirmationToken?: string
  gate?: string
}

export interface PendingApproval {
  requestId: string
  callId: string
  toolName: string
  arguments: Record<string, unknown>
  description: string
}

export interface AgentDecision {
  traceId: string
  runId: string
  caseId: string
  scenarioId: string
  intent: string
  intentConfidence: number
  riskLevel: string
  riskScore: number
  action: string
  conclusion: string
  planTitle: string
  planCopy: string
  refundAmount?: number
  feeAmount?: number
  reply: string
  conversationState: string
  caseStatus: string
  steps: DecisionStep[]
  slots: Record<string, string>
  policyMatches: PolicyMatch[]
  toolSequence: string[]
  ticket?: Ticket
  order: {
    orderId: string
    hotelName: string
    checkIn: string
    checkOut: string
    amount: number
    currency: string
    status: string
    userOnSite: boolean
    policyId: string
    version: number
    roomType: string
    roomCount: number
  }
  verificationPassed: boolean
  verificationViolations: string[]
  hitl?: HitlState
  aiProvider?: string
  agentSessionId?: string
  agentDriven?: boolean
  hasPendingApprovals?: boolean
  pendingApprovals?: PendingApproval[]
  productionMode?: string
}

export interface ToolContract {
  name: string
  mode: string
  purpose: string
  allowedConversationStates?: string[]
}
