import { useEffect, useState } from 'react'
import { api, ApiError } from '../api'
import type { SessionSummary } from '../types'

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
  if (error) return <div className="error">{error}</div>
  return (
    <ul className="session-list">
      {sessions.map(s => (
        <li key={s.sessionId} className={s.sessionId === activeSession ? 'active' : ''}
            onClick={() => onSelect(s.sessionId)}>
          <div>{s.sessionId.slice(0, 12)}</div>
          <div className="meta">{s.messageCount} 条 · {s.lastAt}</div>
        </li>
      ))}
    </ul>
  )
}
