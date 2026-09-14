import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

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
      '/uploads': {
        target: 'http://127.0.0.1:5080',
        changeOrigin: true,
        timeout: 60000,
        proxyTimeout: 60000,
      },
      '/branding': {
        target: 'http://127.0.0.1:5080',
        changeOrigin: true,
        timeout: 60000,
        proxyTimeout: 60000,
      },
    },
  },
})
