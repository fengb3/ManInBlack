import { useState, type FormEvent } from 'react'
import { api } from '../api'

export default function Login({ onLoggedIn }: { onLoggedIn: () => void }) {
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const submit = async (e: FormEvent) => {
    e.preventDefault()
    try {
      const res = await api.login(password)
      if (res.ok) onLoggedIn()
      else setError('密码错误')
    }
    catch {
      setError('网络错误,请重试')
    }
  }
  return (
    <form className="login" onSubmit={submit}>
      <h1>ManInBlack Dashboard</h1>
      <input type="password" value={password} placeholder="密码"
             onChange={e => setPassword(e.target.value)} />
      <button type="submit">登录</button>
      {error && <div className="error">{error}</div>}
    </form>
  )
}
