import { useState } from 'react'
import ReactMarkdown from 'react-markdown'
import type { MessageView as MV, MessageBlock } from '../types'
import { ROLE_LABEL, copyText } from '../utils'

const CHEV = (<svg className="b-chev" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2.2} strokeLinecap="round" strokeLinejoin="round"><path d="m9 6 6 6-6 6" /></svg>)
const COPY = (<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round"><rect x="9" y="9" width="11" height="11" rx="2" /><path d="M5 15V5a2 2 0 0 1 2-2h10" /></svg>)

// 工具调用 / 工具结果 / 未知:可折叠卡片,头部带复制;默认折叠
function ToolBlock({ label, name, status, json }: {
  label: string; name?: string; status?: 'ok' | 'err' | 'unknown'; json: string
}) {
  const [open, setOpen] = useState(false)
  const [copied, setCopied] = useState(false)
  const statusText = status === 'ok' ? '成功' : status === 'err' ? '失败' : status === 'unknown' ? '未知类型' : null
  return (
    <div className={'block' + (open ? ' open' : '')}>
      <div className="block-head" onClick={() => setOpen(o => !o)}>
        {CHEV}<span className="b-label">{label}</span>
        {name && <span className="b-name">{name}</span>}
        {statusText && <span className={'b-status ' + status}>{statusText}</span>}
        <span className="b-spacer" />
        <button className={'copy-btn' + (copied ? ' done' : '')} title="复制 JSON"
                onClick={e => { e.stopPropagation(); copyText(json); setCopied(true); setTimeout(() => setCopied(false), 1200) }}>
          {COPY}
        </button>
      </div>
      <div className="block-body"><pre>{json}</pre></div>
    </div>
  )
}

// 思考链(reasoning):默认折叠的「思考」卡片,暗化正文
function ReasoningBlock({ text }: { text: string }) {
  const [open, setOpen] = useState(false)
  return (
    <div className={'block reasoning' + (open ? ' open' : '')}>
      <div className="block-head" onClick={() => setOpen(o => !o)}>
        {CHEV}<span className="b-label">思考</span>
      </div>
      <div className="block-body reasoning-body">
        <div className="text"><ReactMarkdown>{text}</ReactMarkdown></div>
      </div>
    </div>
  )
}

function BlockView({ block }: { block: MessageBlock }) {
  switch (block.kind) {
    case 'text':
      return <div className="text"><ReactMarkdown>{block.text || ''}</ReactMarkdown></div>
    case 'reasoning':
      return <ReasoningBlock text={block.text || ''} />
    case 'toolCall':
      return <ToolBlock label="工具调用" name={block.toolName} json={block.argumentsJson || ''} />
    case 'toolResult':
      return <ToolBlock label="工具结果" json={block.resultJson || ''} />
    default:
      return <ToolBlock label="未知块" status="unknown" json={block.rawJson || ''} />
  }
}

export default function MessageView({ message }: { message: MV }) {
  const cls = 'role-' + (message.role || 'system')
  return (
    <div className="msg">
      <span className={'role ' + cls}><span className="rd" />{ROLE_LABEL[message.role] || message.role}</span>
      <div className="msg-body">
        {message.blocks.map((b, i) => <BlockView key={i} block={b} />)}
      </div>
    </div>
  )
}
