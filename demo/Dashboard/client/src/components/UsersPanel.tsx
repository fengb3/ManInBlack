import { useEffect, useState } from 'react'
import { api } from '../api'
import type { UserSummary } from '../types'

export default function UsersPanel({ onSelect }: { onSelect: (s: string) => void }) {
  const [users, setUsers] = useState<UserSummary[]>([])
  const [error, setError] = useState('')
  useEffect(() => { api.users().then(setUsers).catch(e => setError(String(e))) }, [])
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
