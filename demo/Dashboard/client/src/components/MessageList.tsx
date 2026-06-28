import { useEffect, useRef, useState } from 'react'
import { api, ApiError } from '../api'
import type { MessageView as MV, SessionSummary } from '../types'
import MessageViewComp from './MessageView'
import { toMs, fmtDate, copyText } from '../utils'

const COPY = (<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round"><rect x="9" y="9" width="11" height="11" rx="2" /><path d="M5 15V5a2 2 0 0 1 2-2h10" /></svg>)
const ERR_ICON = (<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.5} strokeLinecap="round"><path d="M12 9v4M12 17h.01" /><path d="M10.3 4.3 2.6 18a2 2 0 0 0 1.7 3h15.4a2 2 0 0 0 1.7-3L13.7 4.3a2 2 0 0 0-3.4 0Z" /></svg>)

export default function MessageList({ sessionId, summary, onDbError }: {
  sessionId: string
  summary: SessionSummary | null
  onDbError: () => void
}) {
  const [messages, setMessages] = useState<MV[] | null>(null) // null = 加载中
  const [error, setError] = useState('')
  const [copied, setCopied] = useState(false)
  const bottomRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    setMessages(null); setError('')
    api.messages(sessionId)
      .then(setMessages)
      .catch(e => { if (e instanceof ApiError && e.status === 503) onDbError(); else setError(String(e)) })
  }, [sessionId, onDbError])

  // 自动滚到底(spec §9)
  useEffect(() => { bottomRef.current?.scrollIntoView({ behavior: 'smooth' }) }, [messages])

  const first = summary ? toMs(summary.firstAt) : null
  const last = summary ? toMs(summary.lastAt) : null
  const orphan = !summary?.userId

  const doCopy = async () => { await copyText(sessionId); setCopied(true); setTimeout(() => setCopied(false), 1200) }

  return (
    <>
      <div className="m-header">
        <span className={'mh-user' + (orphan ? ' orphan' : '')}>
          <span className="u-dot" />{summary ? (orphan ? '无用户' : summary.userId) : '…'}
        </span>
        {summary && (
          <div className="mh-stats">
            <div className="mh-stat"><span className="k">消息</span><span className="v">{summary.messageCount} 条</span></div>
            {first != null && last != null && (
              <div className="mh-stat"><span className="k">时间范围</span><span className="v mono">{fmtDate(first)} → {fmtDate(last)}</span></div>
            )}
          </div>
        )}
        <div className="mh-id">
          <span className="id-val" title={sessionId}>{sessionId}</span>
          <button className={'copy-btn' + (copied ? ' done' : '')} title="复制会话 ID" onClick={doCopy}>{COPY}</button>
        </div>
      </div>

      {error
        ? <div className="m-state"><div className="ph">{ERR_ICON}</div><h2>加载失败</h2><p>{error}</p></div>
        : messages === null
          ? <div className="m-state"><div className="spin" /></div>
          : messages.length === 0
            ? <div className="m-state"><h2>无消息</h2><p>该会话没有可显示的消息(可能均为损坏 JSON 已被跳过)。</p></div>
            : (
              <div className="stream">
                {messages.map((m, i) => <MessageViewComp key={i} message={m} />)}
                <div ref={bottomRef} />
              </div>
            )}
    </>
  )
}
