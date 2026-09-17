import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import fs from 'node:fs'
import path from 'node:path'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    host: '0.0.0.0',
    port: 5175,
    strictPort: true,
    hmr: {
      host: '192.168.0.109',
      clientPort: 5175,
    },
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:5080',
        changeOrigin: true,
        timeout: 60000,
        proxyTimeout: 60000,
      },
      // Custom logos live on the API; the default pack ships in public/branding and must keep
      // working on the login page even when the API is not running yet.
      '/branding': {
        target: 'http://127.0.0.1:5080',
        changeOrigin: true,
        timeout: 60000,
        proxyTimeout: 60000,
        bypass(req) {
          const urlPath = (req.url ?? '').split('?')[0]
          if (!urlPath.startsWith('/branding/')) return
          const local = path.join(process.cwd(), 'public', urlPath)
          if (fs.existsSync(local)) return urlPath
        },
      },
    },
  },
})
