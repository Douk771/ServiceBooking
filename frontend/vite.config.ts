import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The API's dev port. Defaults to 5000 (what docker-compose maps it to), but macOS grabs 5000 for
// the AirPlay receiver, so it has to be overridable without editing this file.
const apiTarget = process.env.VITE_API_TARGET ?? 'http://localhost:5000'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: apiTarget,
        changeOrigin: true,
      },
      // Company logos and other files served by the API's static file middleware
      // (see Program.cs UseStaticFiles) — without this, uploaded images 404 in dev
      // and Vite's SPA history fallback serves index.html instead.
      '/uploads': {
        target: apiTarget,
        changeOrigin: true,
      },
    },
  },
})
