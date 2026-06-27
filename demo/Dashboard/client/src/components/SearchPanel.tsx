import { useState, type FormEvent } from 'react'
import { api, ApiError } from '../api'
import type { SearchResult } from '../types'

export default function SearchPanel({ onSelect, onDbError }: { onSelect: (s: string) => void; onDbError: () => void }) {
  const [q, setQ] = useState('')
  const [results, setResults] = useState<SearchResult[]>([])
  const [error, setError] = useState('')
  const search = async (e: FormEvent) => {
    e.preventDefault()
    if (!q.trim()) return
    setError('')
    try {
      setResults(await api.search(q))
    } catch (ex) {
      if (ex instanceof ApiError && ex.status === 503) onDbError()
      else setError(String(ex))
    }
  }
  return (
    <div>
      <form onSubmit={search} style={{ display: 'flex', gap: 4, padding: 8 }}>
        <input value={q} onChange={e => setQ(e.target.value)} placeholder="搜索内容…" />
        <button>搜</button>
      </form>
      {error && <div className="error">{error}</div>}
      <ul className="session-list">
        {results.map((r, i) => (
          <li key={i} onClick={() => onSelect(r.sessionId)}>
            <div>{r.sessionId.slice(0, 12)} <span className="meta">· {r.createdAt}</span></div>
            <pre className="snippet">{r.snippet}</pre>
          </li>
        ))}
      </ul>
    </div>
  )
}
