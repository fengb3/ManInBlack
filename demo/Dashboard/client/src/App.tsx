import { useEffect, useState } from 'react'
import { api } from './api'
import Login from './components/Login'
import Dashboard from './components/Dashboard'

export default function App() {
  const [authed, setAuthed] = useState<boolean | null>(null)
  useEffect(() => { api.me().then(r => setAuthed(r.authenticated)).catch(() => setAuthed(false)) }, [])
  if (authed === null) return <div className="loading">加载中…</div>
  if (!authed) return <Login onLoggedIn={() => setAuthed(true)} />
  return <Dashboard onLogout={async () => { await api.logout(); setAuthed(false) }} />
}
