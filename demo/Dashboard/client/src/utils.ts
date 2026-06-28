// 时间/复制/角色辅助(移植自原型 app.html,适配后端 ISO 字符串)

/** session ID 末尾的 10~13 位数字 → 毫秒(unix 秒/毫秒自适应) */
export function tsFromId(id: string): number | null {
  const m = String(id).match(/(\d{10,13})$/)
  if (!m) return null
  const n = +m[1]
  return n > 1e12 ? n : n * 1000
}

/** 后端 ISO 字符串("O" 格式)→ 毫秒;无法解析返回 null */
export function toMs(iso: string | null | undefined): number | null {
  if (!iso) return null
  const t = Date.parse(iso)
  return Number.isNaN(t) ? null : t
}

export function fmtRel(ms: number): string {
  const s = (Date.now() - ms) / 1000
  if (s < 60) return '刚刚'
  if (s < 3600) return Math.floor(s / 60) + ' 分钟前'
  if (s < 86400) return Math.floor(s / 3600) + ' 小时前'
  if (s < 2592000) return Math.floor(s / 86400) + ' 天前'
  return fmtDate(ms)
}

export function fmtDate(ms: number): string {
  const d = new Date(ms)
  return (d.getMonth() + 1) + '-' + String(d.getDate()).padStart(2, '0') + ' ' +
    String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0')
}

export function fmtFull(ms: number): string {
  const d = new Date(ms)
  return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0') + ' ' +
    String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0') + ':' + String(d.getSeconds()).padStart(2, '0')
}

/** 列表行右侧的小时间戳(M/D 换行 HH:mm) */
export function fmtStamp(ms: number): string {
  const d = new Date(ms)
  return String(d.getMonth() + 1).padStart(2, '0') + '/' + String(d.getDate()).padStart(2, '0') + '\n' +
    String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0')
}

/** 复制到剪贴板,带 execCommand 兜底 */
export async function copyText(text: string): Promise<void> {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text)
      return
    }
  } catch { /* 走兜底 */ }
  const ta = document.createElement('textarea')
  ta.value = text
  ta.style.position = 'fixed'
  ta.style.opacity = '0'
  document.body.appendChild(ta)
  ta.select()
  try { document.execCommand('copy') } catch { /* 忽略 */ }
  document.body.removeChild(ta)
}

export const ROLE_LABEL: Record<string, string> = {
  user: '用户', assistant: '助手', tool: '工具', system: '系统',
}
