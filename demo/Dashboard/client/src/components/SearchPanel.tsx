import { useEffect, useRef, useState } from 'react'
import { api, ApiError } from '../api'
import type { SearchResult } from '../types'
import { toMs, fmtFull } from '../utils'

const SEARCH_ICON = (<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.5}><circle cx="11" cy="11" r="7" /><path d="m21 21-4.3-4.3" strokeLinecap="round" /></svg>)

// 命中片段高亮(大小写不敏感,正则元字符转义)
function Highlight({ text, q }: { text: string; q: string }) {
  if (!q) return <>{text}</>
  const escaped = q.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const parts = text.split(new RegExp(`(${escaped})`, 'gi'))
  return <>{parts.map((p, i) => i % 2 === 1 ? <mark key={i}>{p}</mark> : <span key={i}>{p}</span>)}</>
}

export default function SearchPanel({ activeSession, onSelect, onDbError }: {
  activeSession: string | null
  onSelect: (id: string) => void
  onDbError: () => void
}) {
  const [q, setQ] = useState('')
  // null = 未搜(空查询);数组 = 已搜;空数组 = 无结果
  const [results, setResults] = useState<SearchResult[] | null>(null)
  const [error, setError] = useState('')
  const timer = useRef<number | undefined>(undefined)

  useEffect(() => {
    window.clearTimeout(timer.current)
    const query = q.trim()
    if (!query) { setResults(null); setError(''); return }
    timer.current = window.setTimeout(() => {
      api.search(query)
        .then(setResults)
        .catch(e => { if (e instanceof ApiError && e.status === 503) onDbError(); else setError(String(e)) })
    }, 140)
    return () => window.clearTimeout(timer.current)
  }, [q, onDbError])

  return (
    <>
      <div className="search-box">
        <span className="ico">{SEARCH_ICON}</span>
        <input type="search" placeholder="跨会话搜索消息正文…" aria-label="搜索"
               value={q} onChange={e => setQ(e.target.value)} />
      </div>
      <div style={{ flex: 1, minHeight: 0, overflowY: 'auto' }}>
        {error
          ? <div className="state"><div className="s-title">搜索失败</div><div className="s-sub">{error}</div></div>
          : results === null
            ? <div className="state">{SEARCH_ICON}<div className="s-title">输入关键词搜索</div><div className="s-sub">跨所有会话的消息正文做 LIKE 匹配,命中后点击结果即可在主区打开该会话。</div></div>
            : results.length === 0
              ? <div className="state">{SEARCH_ICON}<div className="s-title">无结果</div><div className="s-sub">没有会话的消息正文包含「{q.trim()}」。换个关键词试试。</div></div>
              : results.map((r, i) => {
                const t = toMs(r.createdAt)
                return (
                  <div key={i} className={'sr' + (r.sessionId === activeSession ? ' active' : '')}
                       onClick={() => onSelect(r.sessionId)}>
                    <div className="sr-top">
                      <span className="sr-id" title={r.sessionId}>{r.sessionId}</span>
                      {t != null && <span className="sr-time">{fmtFull(t)}</span>}
                    </div>
                    <div className="sr-snip"><Highlight text={r.snippet} q={q.trim()} /></div>
                  </div>
                )
              })}
      </div>
    </>
  )
}
