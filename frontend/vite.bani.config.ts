import { fileURLToPath } from 'node:url'
import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from 'tailwindcss'
import autoprefixer from 'autoprefixer'

// bani.ezbook.ru build (ARCHITECTURE_CYCLE42.md §42.13.2). Same env contract as vite.config.ts
// (process env > repo-root `.env` > default), only the port knob differs: SB_BANI_WEB_PORT (5176).
// Output goes to ../dist-bani and is merged into dist/__bani by scripts/merge-site-dist.mjs.
export default defineConfig(({ mode }) => {
  const frontendRoot = fileURLToPath(new URL('.', import.meta.url))
  const repoRoot = fileURLToPath(new URL('..', import.meta.url))
  const fileEnv = loadEnv(mode, repoRoot, ['VITE_', 'SB_'])
  const env = (key: string) => {
    const fromProcess = process.env[key]
    if (fromProcess !== undefined && fromProcess.trim() !== '') return fromProcess.trim()
    const fromFile = fileEnv[key]
    if (fromFile !== undefined && fromFile.trim() !== '') return fromFile.trim()
    return undefined
  }
  const parsePort = (value: string | undefined, fallback: number): number => {
    if (value === undefined) return fallback
    const parsed = Number(value)
    if (!Number.isInteger(parsed) || parsed < 1 || parsed > 65535) return fallback
    return parsed
  }

  const apiPort = parsePort(env('SB_API_PORT'), 5000)
  const apiTarget = env('VITE_API_TARGET') ?? `http://localhost:${apiPort}`
  const webPort = parsePort(env('SB_BANI_WEB_PORT'), 5176)

  return {
    root: 'bani',
    publicDir: 'public',
    // The repo-root `.env` (VITE_SMARTCAPTCHA_SITEKEY etc.) is read the same way as for ezbook.
    envDir: repoRoot,
    plugins: [react()],
    resolve: {
      alias: {
        '@bani': fileURLToPath(new URL('./bani/src', import.meta.url)),
        '@': fileURLToPath(new URL('./src', import.meta.url)),
      },
    },
    css: {
      postcss: {
        plugins: [tailwindcss({ config: `${frontendRoot}tailwind.bani.config.js` }), autoprefixer()],
      },
    },
    build: {
      outDir: '../dist-bani',
      emptyOutDir: true,
    },
    server: {
      port: webPort,
      strictPort: false,
      proxy: {
        '/api': { target: apiTarget, changeOrigin: true },
        '/uploads': { target: apiTarget, changeOrigin: true },
      },
    },
  }
})
