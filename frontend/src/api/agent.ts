import axios from 'axios'
import type { AgentDecision, Scenario, ToolContract } from '@/types'

// Prefer same-origin + Vite proxy so Cloud/port-forward previews work.
// Override with VITE_API_BASE_URL only when API is on another origin.
const http = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || '',
  timeout: 30000,
})

const apiKey = import.meta.env.VITE_API_KEY as string | undefined
if (apiKey) {
  http.interceptors.request.use((config) => {
    config.headers['X-Api-Key'] = apiKey
    return config
  })
}

export async function fetchHosting() {
  const { data } = await http.get<{ demoEnabled: boolean; authRequired: boolean }>('/api/hosting')
  return data
}

export async function fetchScenarios() {
  const { data } = await http.get<Scenario[]>('/api/scenarios')
  return data
}

export async function fetchTools() {
  const { data } = await http.get<ToolContract[]>('/api/tools')
  return data
}

export async function runAgentMessage(payload: Record<string, unknown>) {
  const { data } = await http.post<AgentDecision>('/api/agent/message', payload)
  return data
}

export async function respondToApproval(payload: {
  sessionId: string
  requestId: string
  approved: boolean
  reason?: string
}) {
  const { data } = await http.post<AgentDecision>('/api/agent/approvals', payload)
  return data
}

export async function runEval() {
  const { data } = await http.post('/api/eval/run')
  return data as { total: number; passed: number; failed: number; results: unknown[] }
}

export async function runAllWorkflows() {
  const { data } = await http.post('/api/workflows/run-all')
  return data as {
    total: number
    succeeded: number
    failed: number
    results: Array<{ scenarioId: string; succeeded: boolean; caseStatus: string; toolCalls: string[] }>
  }
}

export async function health() {
  const { data } = await http.get('/health')
  return data
}
