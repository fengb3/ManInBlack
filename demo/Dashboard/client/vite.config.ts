import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// 后端地址:Aspire 经 VITE_API_BASE_URL 注入;单独 `npm run dev` 时回落 :5080。
const api = process.env['VITE_API_BASE_URL'] ?? 'http://localhost:5080'
// 端口:Aspire 的 AddViteApp 注入 PORT;单独跑时回落 5173。
const port = Number(process.env['PORT']) || 5173

export default defineConfig({
  plugins: [react()],
  server: { port, host: true, proxy: { '/api': api } },
  build: { outDir: '../wwwroot', emptyOutDir: true },
})
