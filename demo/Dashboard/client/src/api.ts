import type { SessionSummary, UserSummary, MessageView, SearchResult } from './types'

async function get<T>(path: string): Promise<T> {
  const res = await fetch(path, { credentials: 'include' })
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`)
  return res.json() as Promise<T>
}

export const api = {
  me: () => get<{ authenticated: boolean }>('/api/me'),
  login: (password: string) => fetch('/api/login', {
    method: 'POST', credentials: 'include',
    headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ password }),
  }),
  logout: () => fetch('/api/logout', { method: 'POST', credentials: 'include' }),
  sessions: () => get<SessionSummary[]>('/api/sessions'),
  messages: (sessionId: string) =>
    get<MessageView[]>(`/api/sessions/${encodeURIComponent(sessionId)}/messages`),
  users: () => get<UserSummary[]>('/api/users'),
  search: (q: string) => get<SearchResult[]>(`/api/search?q=${encodeURIComponent(q)}`),
}
