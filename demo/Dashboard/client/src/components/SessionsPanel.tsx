import { useEffect, useMemo, useState } from 'react'
import { api, ApiError } from '../api'
import type { SessionSummary } from '../types'

// 会话按用户分组展示(合并原 会话/用户 两个 tab)。
export default function SessionsPanel({ activeSession, onSelect, onDbError }: {
  activeSession: string | null; onSelect: (s: string) => void; onDbError: () => void
}) {
  const [sessions, setSessions] = useState<SessionSummary[]>([])
  const [error, setError] = useState('')
  useEffect(() => {
    api.sessions().then(setSessions).catch(e => {
      if (e instanceof ApiError && e.status === 503) onDbError()
      else setError(String(e))
    })
  }, [onDbError])

  // SessionSummary 已带 userId,按它分组;组内按末次时间倒序。
  const byUser = useMemo(() => {
    const m = new Map<string, SessionSummary[]>()
    for (const s of sessions) {
      const key = s.userId || '(无用户)'
      const arr = m.get(key)
      if (arr) arr.push(s)
      else m.set(key, [s])
    }
    return [...m.entries()].sort((a, b) => b[1].length - a[1].length)
  }, [sessions])

  if (error) return <div className="error">{error}</div>
  if (!sessions.length) return <div className="loading">无会话</div>

  return (
    <div className="sessions-by-user">
      {byUser.map(([user, sess]) => (
        <details key={user} open className="user-group">
          <summary>{user} <span className="meta">({sess.length})</span></summary>
          <ul className="session-list">
            {sess.map(s => (
              <li key={s.sessionId} className={s.sessionId === activeSession ? 'active' : ''}
                  onClick={() => onSelect(s.sessionId)}>
                <div className="sid">{s.sessionId}</div>
                <div className="meta">{s.messageCount} 条 · {s.lastAt}</div>
              </li>
            ))}
          </ul>
        </details>
      ))}
    </div>
  )
}
