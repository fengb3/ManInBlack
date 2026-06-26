import { useEffect, useState } from 'react'
import { api } from '../api'
import type { MessageView } from '../types'
import MessageViewComp from './MessageView'

export default function MessageList({ sessionId }: { sessionId: string }) {
  const [messages, setMessages] = useState<MessageView[]>([])
  const [error, setError] = useState('')
  useEffect(() => {
    setError(''); setMessages([])
    api.messages(sessionId).then(setMessages).catch(e => setError(String(e)))
  }, [sessionId])
  if (error) return <div className="error">无法加载:{error}</div>
  return <div>{messages.map((m, i) => <MessageViewComp key={i} message={m} />)}</div>
}
