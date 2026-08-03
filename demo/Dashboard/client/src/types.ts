export type MessageBlockKind = 'text' | 'toolCall' | 'toolResult' | 'reasoning' | 'unknown'

export interface MessageBlock {
  kind: MessageBlockKind
  text?: string; toolName?: string; argumentsJson?: string; resultJson?: string; rawJson?: string
}
export interface MessageView { role: string; blocks: MessageBlock[] }
export interface SessionSummary {
  sessionId: string; messageCount: number; firstAt: string; lastAt: string; userId?: string | null
  source: number
}
export interface UserSummary { userId: string; createdAt: string }
export interface SearchResult { sessionId: string; snippet: string; createdAt: string }
