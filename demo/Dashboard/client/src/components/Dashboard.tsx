import { useEffect, useState } from 'react'
import { api, ApiError } from '../api'
import type { SessionSummary } from '../types'
import SessionsPanel from './SessionsPanel'
import SearchPanel from './SearchPanel'
import MessageList from './MessageList'

type Tab = 'sessions' | 'search'
type Theme = 'light' | 'dark'

const HAT = (
  <svg viewBox="0 0 24 24" fill="none">
    <path d="M5 13.5c0-.3.2-.5.5-.5h13a.5.5 0 0 1 .5.5v.5H5v-.5Z" fill="#fff" />
    <path d="M7 13.5c0-3 1.5-5 5-5s5 2 5 5" stroke="#fff" strokeWidth={1.8} strokeLinecap="round" />
    <path d="M5.5 14h13" stroke="#fff" strokeWidth={2} strokeLinecap="round" />
  </svg>
)
const MENU = (<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round"><path d="M4 7h16M4 12h16M4 17h16" /></svg>)
const SUN = (<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round"><circle cx="12" cy="12" r="4" /><path d="M12 2v2M12 20v2M2 12h2M20 12h2M5 5l1.5 1.5M17.5 17.5L19 19M19 5l-1.5 1.5M6.5 17.5L5 19" /></svg>)
const MOON = (<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round"><path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8Z" /></svg>)
const LOGOUT = (<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round"><path d="M15 12H4M9 7l-5 5 5 5" /><path d="M10 4h7a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-7" strokeLinejoin="round" /></svg>)
const ALERT = (<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round"><path d="M12 9v4M12 17h.01" /><path d="M10.3 4.3 2.6 18a2 2 0 0 0 1.7 3h15.4a2 2 0 0 0 1.7-3L13.7 4.3a2 2 0 0 0-3.4 0Z" /></svg>)
const CHAT = (<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8}><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2Z" strokeLinejoin="round" /></svg>)
const SEARCH = (<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8}><circle cx="11" cy="11" r="7" /><path d="m21 21-4.3-4.3" strokeLinecap="round" /></svg>)

export default function Dashboard({ theme, onToggleTheme, onLogout }: {
  theme: Theme; onToggleTheme: () => void; onLogout: () => void
}) {
  const [tab, setTab] = useState<Tab>('sessions')
  const [activeId, setActiveId] = useState<string | null>(null)
  const [dbError, setDbError] = useState(false)
  const [sidebarOpen, setSidebarOpen] = useState(false)

  // sessions 上提:供 SessionsPanel 渲染 + MessageList 头部元信息
  const [sessions, setSessions] = useState<SessionSummary[] | null>(null)
  const [sessionsError, setSessionsError] = useState('')

  useEffect(() => {
    api.sessions()
      .then(setSessions)
      .catch(e => {
        if (e instanceof ApiError && e.status === 503) setDbError(true)
        else setSessionsError(String(e))
      })
  }, [])

  const activeSummary = sessions?.find(s => s.sessionId === activeId) ?? null
  const selectSession = (id: string) => { setActiveId(id); setSidebarOpen(false) }

  return (
    <div className="app">
      <header className="topbar">
        <button className="icon-btn menu-btn" aria-label="打开侧栏" onClick={() => setSidebarOpen(true)}>{MENU}</button>
        <div className="brand">
          <span className="logo" aria-hidden="true">{HAT}</span>
          <h1>ManInBlack Dashboard</h1>
          <span className="demo-pill">只读</span>
        </div>
        <div className="spacer" />
        <button className="icon-btn" aria-label="切换暗色模式" title="切换暗色模式" onClick={onToggleTheme}>
          {theme === 'dark' ? MOON : SUN}
        </button>
        <button className="logout-btn" onClick={onLogout}>{LOGOUT} 退出</button>
      </header>

      <div className={'banner' + (dbError ? ' show' : '')} role="alert">
        {ALERT}
        <span className="b-text">无法读取数据库 · 数据端点返回 503。已显示的内容为缓存,新请求可能失败。</span>
        <button className="b-close" onClick={() => setDbError(false)}>关闭</button>
      </div>

      <div className="body">
        <div className={'scrim' + (sidebarOpen ? ' show' : '')} onClick={() => setSidebarOpen(false)} />
        <aside className={'sidebar' + (sidebarOpen ? ' open' : '')}>
          <nav className="tabs" role="tablist">
            <button className={'tab' + (tab === 'sessions' ? ' active' : '')}
              onClick={() => { setTab('sessions'); setSidebarOpen(false) }}>{CHAT} 会话</button>
            <button className={'tab' + (tab === 'search' ? ' active' : '')}
              onClick={() => { setTab('search'); setSidebarOpen(false) }}>{SEARCH} 搜索</button>
          </nav>

          <div className={'panel' + (tab === 'sessions' ? ' active' : '')}>
            <SessionsPanel sessions={sessions} error={sessionsError}
              activeSession={activeId} onSelect={selectSession} />
          </div>
          <div className={'panel search-panel' + (tab === 'search' ? ' active' : '')}>
            <SearchPanel activeSession={activeId} onSelect={selectSession} onDbError={() => setDbError(true)} />
          </div>
        </aside>

        <main className="main">
          {activeId
            ? <MessageList sessionId={activeId} summary={activeSummary} onDbError={() => setDbError(true)} />
            : (
              <div className="m-state">
                <div className="ph">{CHAT}</div>
                <h2>选择一个会话</h2>
                <p>在左侧「会话」标签中按用户展开并点选一条,或切到「搜索」跨会话查找消息。</p>
              </div>
            )}
        </main>
      </div>
    </div>
  )
}
