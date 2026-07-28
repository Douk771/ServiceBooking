import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
      // Company logos and other files served by the API's static file middleware
      // (see Program.cs UseStaticFiles) — without this, uploaded images 404 in dev
      // and Vite's SPA history fallback serves index.html instead.
      '/uploads': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
})
