import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import { fileURLToPath } from 'node:url'

// Separate from vite.config.ts on purpose (ARCHITECTURE.md §15.2) — the prod build and the test run
// shouldn't share a config file just because they happen to both use Vite/esbuild under the hood.
export default defineConfig({
  plugins: [react()],
  // Cycle 23 (ARCHITECTURE_CYCLE23.md §399.1): goods tests import shared modules via `@/` and their own via `@goods/`.
  resolve: {
    alias: {
      '@goods': fileURLToPath(new URL('./goods/src', import.meta.url)),
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  test: {
    environment: 'jsdom',
    globals: false, // explicit imports of describe/it/expect — same convention as named exports in the app code
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}', 'goods/src/**/*.test.{ts,tsx}'],
    css: false,
  },
})
