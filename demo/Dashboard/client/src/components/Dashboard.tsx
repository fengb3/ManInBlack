import { useState, useCallback } from 'react'
import SessionsPanel from './SessionsPanel'
import UsersPanel from './UsersPanel'
import SearchPanel from './SearchPanel'
import MessageList from './MessageList'

type Tab = 'sessions' | 'users' | 'search'

export default function Dashboard({ onLogout }: { onLogout: () => void }) {
  const [tab, setTab] = useState<Tab>('sessions')
  const [activeSession, setActiveSession] = useState<string | null>(null)
  // spec §10: 数据端点 503 时顶部显示横幅
  const [dbError, setDbError] = useState(false)
  const handleDbError = useCallback(() => setDbError(true), [])
  return (
    <div>
      {dbError && <div className="db-error-banner">无法读取数据库</div>}
      <header className="topbar">
        <span>ManInBlack Dashboard</span>
        <button onClick={onLogout}>退出</button>
      </header>
      <div className="body">
        <aside className="sidebar">
          <nav className="tabs">
            <button className={tab === 'sessions' ? 'active' : ''} onClick={() => setTab('sessions')}>会话</button>
            <button className={tab === 'users' ? 'active' : ''} onClick={() => setTab('users')}>用户</button>
            <button className={tab === 'search' ? 'active' : ''} onClick={() => setTab('search')}>搜索</button>
          </nav>
          {tab === 'sessions' && <SessionsPanel activeSession={activeSession} onSelect={setActiveSession} onDbError={handleDbError} />}
          {tab === 'users' && <UsersPanel onSelect={s => { setActiveSession(s); setTab('sessions') }} onDbError={handleDbError} />}
          {tab === 'search' && <SearchPanel onSelect={setActiveSession} onDbError={handleDbError} />}
        </aside>
        <main className="main">
          {activeSession ? <MessageList sessionId={activeSession} onDbError={handleDbError} /> : <div className="loading">选择一个会话</div>}
        </main>
      </div>
    </div>
  )
}
