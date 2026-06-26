import { useState } from 'react'
import SessionsPanel from './SessionsPanel'
import UsersPanel from './UsersPanel'
import SearchPanel from './SearchPanel'
import MessageList from './MessageList'

type Tab = 'sessions' | 'users' | 'search'

export default function Dashboard({ onLogout }: { onLogout: () => void }) {
  const [tab, setTab] = useState<Tab>('sessions')
  const [activeSession, setActiveSession] = useState<string | null>(null)
  return (
    <div>
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
          {tab === 'sessions' && <SessionsPanel activeSession={activeSession} onSelect={setActiveSession} />}
          {tab === 'users' && <UsersPanel onSelect={s => { setActiveSession(s); setTab('sessions') }} />}
          {tab === 'search' && <SearchPanel onSelect={setActiveSession} />}
        </aside>
        <main className="main">
          {activeSession ? <MessageList sessionId={activeSession} /> : <div className="loading">选择一个会话</div>}
        </main>
      </div>
    </div>
  )
}
