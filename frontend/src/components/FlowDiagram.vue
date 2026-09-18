<template>
  <section class="dflow-shell" aria-label="Agent 完整工作流程">
    <div class="dflow-toolbar">
      <div>
        <strong>完整 Workflow</strong>
        <p>从接收问题到业务闭环，包含权限门、判断、回环和三条执行路径</p>
      </div>
      <div class="dflow-legend" aria-label="技术角色图例">
        <span class="dflow-key">LLM / 规则</span>
        <span class="dflow-key" data-tone="tool">Tool</span>
        <span class="dflow-key" data-tone="guard">权限 / 人工</span>
        <span class="dflow-key" data-tone="memory">Memory / Trace</span>
      </div>
    </div>
    <div class="dflow-viewport" tabindex="0" aria-label="可滚动查看完整流程图">
      <div class="dflow-canvas">
        <div class="dflow-phase" style="top: 410px"><span>02 · 核实订单与事实</span></div>
        <div class="dflow-phase" style="top: 990px"><span>03 · 决策与路由</span></div>
        <div class="dflow-phase" style="top: 2160px"><span>04 · 结果闭环</span></div>
        <svg class="dflow-lines" viewBox="0 0 1180 2460" aria-hidden="true">
          <defs>
            <marker id="flow-arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse">
              <path d="M 0 0 L 10 5 L 0 10 z" fill="context-stroke" />
            </marker>
          </defs>
          <path data-tone="main" d="M590 122 V150" /><path data-tone="main" d="M590 242 V280" /><path data-tone="main" d="M590 372 V393" />
          <path data-tone="main" d="M717 520 H770 V496 H810" /><text x="748" y="506">允许</text>
          <path data-tone="guard" d="M463 520 H28 V1756 H70" /><text x="34" y="540">拒绝</text>
          <path data-tone="main" d="M960 542 V580" /><path data-tone="main" d="M960 672 V698" />
          <path data-tone="loop" d="M843 825 H380" /><text x="602" y="813">否</text>
          <path data-tone="loop" d="M80 806 H28 V520 H463" />
          <path data-tone="main" d="M970 952 V975 H590 V973" /><text x="944" y="970">是</text>
          <path d="M463 1100 H190 V1230" /><text x="292" y="1088">只需查询</text>
          <path data-tone="main" d="M590 1227 V1240" /><text x="604" y="1233">可生成确定方案</text>
          <path data-tone="guard" d="M717 1100 H780 V1740 H717" /><text x="790" y="1120">需协同或高风险</text>
          <path d="M190 1322 V2236 H440" />
          <path data-tone="main" d="M590 1332 V1370" /><path data-tone="main" d="M590 1462 V1500" /><path data-tone="main" d="M590 1592 V1623" />
          <path data-tone="guard" d="M463 1740 H370" /><text x="395" y="1728">拒绝</text>
          <path data-tone="main" d="M590 1867 V1880" /><text x="604" y="1875">交易写入</text>
          <path data-tone="guard" d="M717 1740 H970 V1880" /><text x="825" y="1728">流程写入</text>
          <path data-tone="main" d="M590 1972 V2010" /><path data-tone="main" d="M590 2102 V2190" />
          <path d="M970 1972 V2010" /><path d="M970 2102 V2140 H740 V2236" />
          <path data-tone="main" d="M590 2282 V2320" />
        </svg>

        <article
          v-for="n in nodes"
          :key="n.title + n.top"
          class="dnode"
          :class="{ decision: n.decision }"
          :data-tone="n.tone"
          :style="{ left: n.left + 'px', top: n.top + 'px' }"
        >
          <div :class="n.decision ? 'dnode-content' : undefined">
            <div class="dnode-kicker">
              <span v-if="n.role">{{ n.role }}</span>
              <b>{{ n.type }}</b>
            </div>
            <h2>{{ n.title }}</h2>
            <p v-html="n.body" />
          </div>
        </article>
      </div>
    </div>
    <div class="dflow-summary" aria-label="四层流程摘要">
      <div><span>01 理解</span><strong>识别诉求、原因和紧急度</strong></div>
      <div><span>02 核实</span><strong>确认订单并读取可信事实</strong></div>
      <div><span>03 决策</span><strong>规则选择查询、交易或协同</strong></div>
      <div><span>04 闭环</span><strong>校验结果并持久化案件</strong></div>
    </div>
  </section>
</template>

<script setup lang="ts">
type Tone = 'agent' | 'tool' | 'guard' | 'memory' | undefined
const nodes: Array<{
  left: number; top: number; role?: string; type: string; title: string; body: string; tone?: Tone; decision?: boolean
}> = [
  { left: 440, top: 30, role: '会话入口', type: 'Session Memory', title: '接收用户问题', body: '<code>session_id</code> 与 <code>trace_id</code>', tone: 'memory' },
  { left: 440, top: 150, role: '理解层', type: 'LLM + 安全分类器', title: '理解诉求与紧急度', body: '意图、原因、风险、订单线索', tone: 'agent' },
  { left: 440, top: 280, role: '订单选择组件', type: 'UI', title: '用户确认唯一订单', body: '<code>confirmed_order_id</code> 写入状态' },
  { left: 500, top: 430, type: 'READ Gate', title: '是否允许读取？', body: '身份、归属、状态白名单、脱敏', tone: 'guard', decision: true },
  { left: 810, top: 450, role: '查询类 Tool', type: 'READ', title: '读取可信业务事实', body: '订单、政策、退款、支付、工单、责任链', tone: 'tool' },
  { left: 810, top: 580, role: '结构化过滤', type: 'RAG', title: '匹配政策并辅助解释', body: 'RAG 不决定金额与权限', tone: 'agent' },
  { left: 880, top: 735, type: 'LLM + 必填规则', title: '信息是否足够？', body: '只检查会改变结论的信息', tone: 'agent', decision: true },
  { left: 80, top: 760, role: '对话补全', type: 'LLM + Session Memory', title: '最少追问', body: '只问会改变处理结论的信息，再回到读取权限门', tone: 'memory' },
  { left: 500, top: 1010, type: 'Rules + Workflow', title: '选择处理路径', body: '规则决定金额、权限、风险和路由', tone: 'agent', decision: true },
  { left: 40, top: 1230, role: '查询类 Tool', type: 'READ', title: '查询当前业务状态', body: '退款进度、支付事件、工单进度', tone: 'tool' },
  { left: 440, top: 1240, role: '计算 / 决策 Tool', type: 'READ', title: '生成可执行方案', body: '退款报价、变更报价、保障预览、权限校验', tone: 'tool' },
  { left: 440, top: 1370, role: '结构化组件', type: 'LLM', title: '展示方案与预期', body: '金额沿用 Tool 回执，LLM 只负责解释', tone: 'agent' },
  { left: 440, top: 1500, role: '前端确认组件', type: '安全机制', title: '用户二次确认', body: '明确金额、后果和操作对象', tone: 'guard' },
  { left: 500, top: 1650, type: 'WRITE Gate', title: '允许改变业务状态？', body: '鉴权、状态、风险、确认、版本、幂等', tone: 'guard', decision: true },
  { left: 70, top: 1710, role: '安全机制', type: 'Human-in-the-loop', title: '阻断或转人工核验', body: '不暴露订单，不把失败改写成成功', tone: 'guard' },
  { left: 440, top: 1880, role: '交易写权限', type: 'WRITE Auth', title: '校验确认、版本和风险', body: '<code>confirmation_token</code> + <code>expected_version</code>', tone: 'guard' },
  { left: 440, top: 2010, role: '交易类 Tool', type: 'WRITE', title: '执行取消或订单变更', body: '<code>submit_cancellation</code> / <code>submit_order_change</code>', tone: 'tool' },
  { left: 820, top: 1880, role: '协同写权限', type: 'L3 / L4 硬转人工', title: '允许创建协作任务', body: '只能创建工单，不得裁决退款金额', tone: 'guard' },
  { left: 820, top: 2010, role: '协同类 Tool', type: 'WRITE', title: '创建并跟踪协作任务', body: '供应商、支付调查、材料、人工专席', tone: 'tool' },
  { left: 440, top: 2190, role: '事实校验器', type: 'Verifier + LLM', title: '返回结果与下一步', body: '校验金额、动作词、状态、SLA 与允许按钮', tone: 'agent' },
  { left: 440, top: 2320, role: '案件持久化', type: 'Case Memory + Trace Log', title: '保存案件状态', body: '支持后续查询、恢复会话、审计和 Badcase 回放', tone: 'memory' },
]
</script>

<style scoped>
.dflow-shell { overflow: hidden; }
.dflow-toolbar {
  min-height: 54px; display: flex; align-items: center; justify-content: space-between; gap: 1rem;
  padding: 0.7rem 1rem; border-bottom: 1px solid var(--color-border); background: var(--color-surface-muted);
}
.dflow-toolbar strong { font-size: 12px; }
.dflow-toolbar p { margin: 0.15rem 0 0; color: var(--color-ink-muted); font-size: 11px; }
.dflow-legend { display: flex; flex-wrap: wrap; gap: 0.75rem; color: var(--color-ink-muted); font-size: 11px; }
.dflow-key { display: inline-flex; align-items: center; gap: 0.35rem; }
.dflow-key::before { content: ''; width: 7px; height: 7px; border-radius: 2px; background: var(--color-accent); }
.dflow-key[data-tone="tool"]::before { background: var(--color-success); }
.dflow-key[data-tone="guard"]::before { background: var(--color-danger); }
.dflow-key[data-tone="memory"]::before { background: var(--color-warning); }
.dflow-viewport { overflow: auto; max-height: min(72vh, 900px); background: var(--color-surface); }
.dflow-canvas { position: relative; width: 1180px; height: 2460px; margin: 0 auto; transform: scale(0.82); transform-origin: top center; }
.dflow-lines { position: absolute; inset: 0; width: 1180px; height: 2460px; }
.dflow-lines path { fill: none; stroke: oklch(0.68 0.025 250); stroke-width: 1.5; marker-end: url(#flow-arrow); }
.dflow-lines path[data-tone="main"] { stroke: var(--color-accent); stroke-width: 2; }
.dflow-lines path[data-tone="guard"] { stroke: var(--color-danger); }
.dflow-lines path[data-tone="loop"] { stroke-dasharray: 6 5; }
.dflow-lines text { fill: var(--color-ink-muted); font-size: 11px; paint-order: stroke; stroke: var(--color-surface); stroke-width: 6px; stroke-linejoin: round; }
.dnode {
  position: absolute; z-index: 1; width: 300px; min-height: 92px;
  border: 1px solid var(--color-border); border-radius: 10px; padding: 12px 14px;
  background: var(--color-surface); box-shadow: var(--shadow-panel);
}
.dnode[data-tone="tool"] { border-color: oklch(0.82 0.05 155); background: var(--color-success-soft); }
.dnode[data-tone="guard"] { border-color: oklch(0.84 0.055 28); background: var(--color-danger-soft); }
.dnode[data-tone="memory"] { border-color: oklch(0.84 0.055 72); background: var(--color-warning-soft); }
.dnode[data-tone="agent"] { border-color: oklch(0.80 0.055 252); background: var(--color-accent-soft); }
.dnode-kicker { display: flex; justify-content: space-between; gap: 8px; margin-bottom: 6px; color: var(--color-ink-muted); font-size: 10px; font-weight: 700; }
.dnode-kicker b { color: var(--color-accent-strong); }
.dnode h2 { margin: 0 0 4px; font-size: 14px; }
.dnode p { margin: 0; color: var(--color-ink-muted); font-size: 11px; line-height: 1.45; }
.dnode :deep(code) { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 10px; }
.dnode.decision {
  width: 180px; height: 180px; min-height: 0; display: grid; place-items: center;
  border-radius: 12px; padding: 0; transform: rotate(45deg);
}
.dnode.decision .dnode-content { width: 136px; transform: rotate(-45deg); text-align: center; }
.dnode.decision .dnode-kicker { display: block; margin-bottom: 5px; }
.dnode.decision h2 { font-size: 13px; }
.dnode.decision p { font-size: 10px; }
.dflow-phase {
  position: absolute; z-index: 0; left: 20px; right: 20px; height: 1px;
  border-top: 1px dashed oklch(0.84 0.02 250);
}
.dflow-phase span {
  position: absolute; top: -11px; left: 0; padding-right: 8px;
  background: var(--color-surface); color: var(--color-ink-muted); font-size: 10px; font-weight: 700;
}
.dflow-summary {
  display: grid; grid-template-columns: repeat(4, 1fr);
  border-top: 1px solid var(--color-border); background: var(--color-surface);
}
.dflow-summary div { padding: 13px 15px; border-right: 1px solid var(--color-border); }
.dflow-summary div:last-child { border-right: 0; }
.dflow-summary span { display: block; color: var(--color-ink-muted); font-size: 9px; font-weight: 700; }
.dflow-summary strong { display: block; margin-top: 4px; font-size: 11px; }
@media (max-width: 900px) {
  .dflow-summary { grid-template-columns: 1fr 1fr; }
}
</style>
