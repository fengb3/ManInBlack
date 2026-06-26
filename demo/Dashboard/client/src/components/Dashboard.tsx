export default function Dashboard({ onLogout }: { onLogout: () => void }) {
  return <div><button onClick={onLogout}>退出</button><p>会话/消息面板待实现</p></div>
}
