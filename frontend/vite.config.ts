import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

// Cycle 8 env contract (API_CONTRACT_CYCLE8.md §85): the working-copy `.env` at the repo root is
// optional and shared with docker-compose. Priority, normative:
//   1. process env (e.g. `export VITE_API_TARGET=...` / `export SB_API_PORT=...`)
//   2. value from the working copy's `.env`
//   3. built-in default from the contract table (SB_API_PORT=5000, SB_WEB_PORT=5173)
export default defineConfig(({ mode }) => {
  // Third arg '' loads every var, not just VITE_-prefixed ones, so SB_* is visible too.
  const fileEnv = loadEnv(mode, '..', '')
  const env = (key: string) => process.env[key] ?? fileEnv[key]

  const apiPort = env('SB_API_PORT') ?? '5000'
  // VITE_API_TARGET is the cycle-3 override and always wins outright.
  const apiTarget = env('VITE_API_TARGET') ?? `http://localhost:${apiPort}`
  const webPort = Number(env('SB_WEB_PORT') ?? '5173')

  return {
    plugins: [react()],
    server: {
      port: webPort,
      // Never strictPort (US-89): if SB_WEB_PORT is taken by a neighboring Vite instance, Vite
      // must pick the next free port and print the address instead of failing.
      strictPort: false,
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
  }
})
