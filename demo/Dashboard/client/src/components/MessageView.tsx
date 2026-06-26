import ReactMarkdown from 'react-markdown'
import type { MessageView as MV } from '../types'

export default function MessageView({ message }: { message: MV }) {
  return (
    <div className={`message role-${message.role}`}>
      <span className="role">{message.role}</span>
      <div className="blocks">
        {message.blocks.map((b, i) => {
          switch (b.kind) {
            case 'text': return <ReactMarkdown key={i}>{b.text ?? ''}</ReactMarkdown>
            case 'toolCall': return (
              <details key={i}><summary>▸ 工具调用 {b.toolName}</summary><pre>{b.argumentsJson}</pre></details>)
            case 'toolResult': return (
              <details key={i}><summary>▸ 工具结果</summary><pre>{b.resultJson}</pre></details>)
            default: return (
              <details key={i}><summary>▸ 未知内容</summary><pre>{b.rawJson}</pre></details>)
          }
        })}
      </div>
    </div>
  )
}
