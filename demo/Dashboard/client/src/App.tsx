import { useEffect, useState } from 'react'
import { api } from './api'
import Login from './components/Login'
import Dashboard from './components/Dashboard'

type Theme = 'light' | 'dark'

// 暗色主题:data-theme 写到 <html>,持久化到 localStorage(原型 #06)
function useTheme(): [Theme, () => void] {
  const [theme, setTheme] = useState<Theme>(() => {
    try { return (localStorage.getItem('mib-theme') as Theme) || 'light' } catch { return 'light' }
  })
  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme)
    try { localStorage.setItem('mib-theme', theme) } catch { /* 忽略 */ }
  }, [theme])
  return [theme, () => setTheme(t => (t === 'dark' ? 'light' : 'dark'))]
}

export default function App() {
  const [authed, setAuthed] = useState<boolean | null>(null)
  const [theme, toggleTheme] = useTheme()

  useEffect(() => {
    api.me().then(r => setAuthed(r.authenticated)).catch(() => setAuthed(false))
  }, [])

  if (authed === null) {
    return <div style={{ minHeight: '100vh', display: 'grid', placeItems: 'center' }}><div className="spin" /></div>
  }
  if (!authed) return <Login onLoggedIn={() => setAuthed(true)} />
  return (
    <Dashboard
      theme={theme}
      onToggleTheme={toggleTheme}
      onLogout={async () => { await api.logout(); setAuthed(false) }}
    />
  )
}
