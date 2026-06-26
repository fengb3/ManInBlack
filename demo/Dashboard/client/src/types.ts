export type MessageBlockKind = 'text' | 'toolCall' | 'toolResult' | 'unknown'

export interface MessageBlock {
  kind: MessageBlockKind
  text?: string; toolName?: string; argumentsJson?: string; resultJson?: string; rawJson?: string
}
export interface MessageView { role: string; blocks: MessageBlock[] }
export interface SessionSummary {
  sessionId: string; messageCount: number; firstAt: string; lastAt: string; userId?: string | null
}
export interface UserSummary { userId: string; metadata: Record<string, unknown>; sessionIds: string[] }
export interface SearchResult { sessionId: string; snippet: string; createdAt: string }
