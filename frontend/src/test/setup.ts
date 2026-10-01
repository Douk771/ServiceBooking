// The `/vitest` entry point extends Vitest's own `expect` (imported explicitly, per `globals: false`
// in vitest.config.ts) instead of assuming a global `expect` exists.
import '@testing-library/jest-dom/vitest'
import { afterEach } from 'vitest'

// Testing Library's own auto-cleanup detects the test framework via a global `afterEach` — which
// doesn't exist under `globals: false` (explicit imports everywhere, matching the app's own
// named-export convention). Without this, components from one test stay mounted into the next.
// Cycle 36 (ARCHITECTURE_CYCLE36.md §36.8): files running under `@vitest-environment node` have no
// `document`, so RTL is imported only when a DOM exists — node files don't pay for loading it.
if (typeof document !== 'undefined') {
  const { cleanup } = await import('@testing-library/react')
  afterEach(() => {
    cleanup()
  })
}
