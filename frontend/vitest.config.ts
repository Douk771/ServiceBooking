import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

// Separate from vite.config.ts on purpose (ARCHITECTURE.md §15.2) — the prod build and the test run
// shouldn't share a config file just because they happen to both use Vite/esbuild under the hood.
export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    globals: false, // explicit imports of describe/it/expect — same convention as named exports in the app code
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    css: false,
    coverage: {
      provider: 'v8',
      include: ['src/utils/**', 'src/components/clientNotes/**'],
    },
  },
})
