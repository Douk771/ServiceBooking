import { fileURLToPath } from 'node:url'
import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

// Cycle 8 env contract (API_CONTRACT_CYCLE8.md §85): the working-copy `.env` at the repo root is
// optional and shared with docker-compose. Priority, normative:
//   1. process env (e.g. `export VITE_API_TARGET=...` / `export SB_API_PORT=...`)
//   2. value from the working copy's `.env`
//   3. built-in default from the contract table (SB_API_PORT=5000, SB_WEB_PORT=5173)
export default defineConfig(({ mode }) => {
  // Resolve the repo root relative to THIS FILE, not process.cwd() — loadEnv's envDir arg is
  // joined against cwd internally, so a relative '..' would silently pick up the wrong `.env`
  // when vite is invoked from the repo root (e.g. `npm run dev --prefix frontend`) instead of
  // from `frontend/`. Reviewed in cycle 8 review: frontend/vite.config.ts finding.
  const repoRoot = fileURLToPath(new URL('..', import.meta.url))
  // Restrict to the VITE_ and SB_ prefixes per API_CONTRACT_CYCLE8.md §85.2 — the root `.env`
  // also carries unrelated secrets (e.g. GLITCHTIP_POSTGRES_PASSWORD, GLITCHTIP_SECRET_KEY,
  // consumed by docker-compose.glitchtip.yml), which must never be loaded here.
  const fileEnv = loadEnv(mode, repoRoot, ['VITE_', 'SB_'])
  // An empty value is a normal shape for a hand-edited .env (a knob left blank rather than
  // removed) and must be treated as "not set", not as a literal empty override.
  const env = (key: string) => {
    const fromProcess = process.env[key]
    if (fromProcess !== undefined && fromProcess.trim() !== '') return fromProcess.trim()
    const fromFile = fileEnv[key]
    if (fromFile !== undefined && fromFile.trim() !== '') return fromFile.trim()
    return undefined
  }
  // Parses a port string into a valid 1-65535 port number, falling back to `fallback` for
  // anything empty, non-numeric, non-integer, or out of the valid TCP port range.
  const parsePort = (value: string | undefined, fallback: number): number => {
    if (value === undefined) return fallback
    const parsed = Number(value)
    if (!Number.isInteger(parsed) || parsed < 1 || parsed > 65535) return fallback
    return parsed
  }

  const apiPort = parsePort(env('SB_API_PORT'), 5000)
  // VITE_API_TARGET is the cycle-3 override and always wins outright.
  const apiTarget = env('VITE_API_TARGET') ?? `http://localhost:${apiPort}`
  const webPort = parsePort(env('SB_WEB_PORT'), 5173)

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
