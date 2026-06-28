import type { SessionSummary, UserSummary, MessageView, SearchResult } from './types'

/** 封装 HTTP 错误，携带状态码用于区分 503 等场景 */
export class ApiError extends Error {
  constructor(message: string, public readonly status: number) {
    super(message)
  }
}

async function get<T>(path: string): Promise<T> {
  const res = await fetch(path, { credentials: 'include' })
  if (!res.ok) {
    if (res.status === 503) throw new ApiError('无法读取数据库', 503)
    throw new ApiError(`${res.status} ${res.statusText}`, res.status)
  }
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
