import { useMemo, useState } from 'react'
import type { SessionSummary } from '../types'
import { tsFromId, toMs, fmtRel, fmtStamp } from '../utils'

const CHEV = (<svg className="chev" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2.2} strokeLinecap="round" strokeLinejoin="round"><path d="m9 6 6 6-6 6" /></svg>)
const PREVIEW = 12 // 每组默认前 12 条,其余折叠(原型 #02)

export default function SessionsPanel({ sessions, error, activeSession, onSelect }: {
  sessions: SessionSummary[] | null
  error: string
  activeSession: string | null
  onSelect: (id: string) => void
}) {
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({})
  const [expanded, setExpanded] = useState<Record<string, boolean>>({})

  const groups = useMemo(() => {
    if (!sessions) return []
    const m = new Map<string, SessionSummary[]>()
    for (const s of sessions) {
      const key = s.userId || '__orphan__'
      const arr = m.get(key)
      if (arr) arr.push(s); else m.set(key, [s])
    }
    const entries = [...m.entries()].map(([key, list]) => {
      list.sort((a, b) => (toMs(b.lastAt) ?? 0) - (toMs(a.lastAt) ?? 0))
      return { key, name: key === '__orphan__' ? '无用户' : key, list }
    })
    entries.sort((a, b) => {
      if (a.key === '__orphan__') return 1
      if (b.key === '__orphan__') return -1
      return b.list.length - a.list.length
    })
    return entries
  }, [sessions])

  if (error) {
    return <div className="state"><div className="s-title">加载失败</div><div className="s-sub">{error}</div></div>
  }
  if (sessions === null) return <div className="state"><div className="spin" /></div>
  if (!sessions.length) return <div className="state"><div className="s-title">无会话</div></div>

  return (
    <>
      {groups.map(g => {
        const isCol = !!collapsed[g.key]
        const isExp = !!expanded[g.key]
        const shown = isExp ? g.list : g.list.slice(0, PREVIEW)
        const hidden = g.list.length - shown.length
        return (
          <div key={g.key} className={'group' + (isCol ? ' collapsed' : '')}>
            <div className="group-head" onClick={() => setCollapsed(c => ({ ...c, [g.key]: !c[g.key] }))}>
              {CHEV}<span className="g-name">{g.name}</span><span className="g-count">{g.list.length}</span>
            </div>
            <div className="group-body">
              {shown.map(s => {
                const stamp = tsFromId(s.sessionId)
                const last = toMs(s.lastAt)
                return (
                  <div key={s.sessionId} className={'row' + (s.sessionId === activeSession ? ' active' : '')}
                       title={s.sessionId} onClick={() => onSelect(s.sessionId)}>
                    <span className="r-dot" />
                    <div className="r-main">
                      <div className="r-id" title={s.sessionId}>{s.sessionId}</div>
                      <div className="r-meta">
                        <span className="r-count"><b>{s.messageCount}</b> 条</span>
                        {last != null && <span className="r-time">· {fmtRel(last)}</span>}
                      </div>
                    </div>
                    <span className="r-stamp">{stamp != null ? fmtStamp(stamp) : '—'}</span>
                  </div>
                )
              })}
              {hidden > 0 && (
                <button className="show-more" onClick={() => setExpanded(e => ({ ...e, [g.key]: true }))}>
                  展开剩余 {hidden} 个会话
                </button>
              )}
            </div>
          </div>
        )
      })}
    </>
  )
}
