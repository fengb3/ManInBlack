import { useEffect, useState } from 'react'
import { api, ApiError } from '../api'
import type { UserSummary } from '../types'

export default function UsersPanel({ onSelect, onDbError }: { onSelect: (s: string) => void; onDbError: () => void }) {
  const [users, setUsers] = useState<UserSummary[]>([])
  const [error, setError] = useState('')
  useEffect(() => {
    api.users().then(setUsers).catch(e => {
      if (e instanceof ApiError && e.status === 503) onDbError()
      else setError(String(e))
    })
  }, [onDbError])
  if (error) return <div className="error">{error}</div>
  return (
    <ul className="user-list">
      {users.map(u => (
        <li key={u.userId}>
          <div>{u.userId} <span className="meta">({u.sessionIds.length})</span></div>
          <ul className="session-list">
            {u.sessionIds.map(s => (
              <li key={s} onClick={() => onSelect(s)}>
                <div>{s.slice(0, 12)}</div>
              </li>
            ))}
          </ul>
        </li>
      ))}
    </ul>
  )
}
