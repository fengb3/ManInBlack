import { useEffect, useRef, useState } from 'react'
import { api } from '../api'
import type { MessageView } from '../types'
import { ApiError } from '../api'
import MessageViewComp from './MessageView'

export default function MessageList({ sessionId, onDbError }: { sessionId: string; onDbError: () => void }) {
  const [messages, setMessages] = useState<MessageView[]>([])
  const [error, setError] = useState('')
  // 滚动哨兵：新消息加载后自动滚到底部（spec §9）
  const bottomRef = useRef<HTMLDivElement>(null)
  useEffect(() => { bottomRef.current?.scrollIntoView({ behavior: 'smooth' }) }, [messages])
  useEffect(() => {
    setError(''); setMessages([])
    api.messages(sessionId).then(setMessages).catch(e => {
      if (e instanceof ApiError && e.status === 503) onDbError()
      else setError(String(e))
    })
  }, [sessionId, onDbError])
  if (error) return <div className="error">无法加载:{error}</div>
  return <div>
    {messages.map((m, i) => <MessageViewComp key={i} message={m} />)}
    <div ref={bottomRef} />
  </div>
}
