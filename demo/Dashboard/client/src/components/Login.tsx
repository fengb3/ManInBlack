import { useState, type FormEvent } from 'react'
import { api } from '../api'
import duck from '../assets/duck.png'

// 锁图标
const LOCK = (
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8}>
    <rect x="5" y="11" width="14" height="9" rx="2" />
    <path d="M8 11V8a4 4 0 0 1 8 0v3" />
  </svg>
)

export default function Login({ onLoggedIn }: { onLoggedIn: () => void }) {
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setError('')
    if (!password.trim()) { setError('请输入密码'); return }
    setLoading(true)
    try {
      const res = await api.login(password)
      if (res.ok) onLoggedIn()
      else setError('密码错误')
    } catch {
      setError('网络错误,请重试')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="login-page">
      <main className="login-card" role="main">
        <div className="brand">
          <span className="logo" aria-hidden="true"><img src={duck} alt="" /></span>
          <span className="kicker">只读聊天记录查看器</span>
        </div>

        <h1>ManInBlack Dashboard</h1>
        <p className="lede">输入访问密码以浏览 SQLite 中的会话记录。所有数据端点均需鉴权,严格只读。</p>

        <form onSubmit={submit} noValidate>
          <div className="field">
            <label htmlFor="pw">访问密码</label>
            <div className="input-wrap">
              <span className="lead" aria-hidden="true">{LOCK}</span>
              <input id="pw" type="password" name="password" autoComplete="current-password"
                     placeholder="输入密码" value={password}
                     onChange={e => setPassword(e.target.value)} />
            </div>
          </div>

          <button className="btn" type="submit" disabled={loading}>
            {loading ? <><span className="spin sm" /> 加载中…</> : '登录'}
          </button>

          <div className={'alert' + (error ? ' show' : '')} role="alert">
            <span className="dot" /><span>{error || ' '}</span>
          </div>
        </form>

        <p className="hint">cookie 鉴权 · 严格只读 · 退出即清除登录态</p>
      </main>
    </div>
  )
}
