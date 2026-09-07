// The `/vitest` entry point extends Vitest's own `expect` (imported explicitly, per `globals: false`
// in vitest.config.ts) instead of assuming a global `expect` exists.
import '@testing-library/jest-dom/vitest'
import { afterEach } from 'vitest'
import { cleanup } from '@testing-library/react'

// Testing Library's own auto-cleanup detects the test framework via a global `afterEach` — which
// doesn't exist under `globals: false` (explicit imports everywhere, matching the app's own
// named-export convention). Without this, components from one test stay mounted into the next.
afterEach(() => {
  cleanup()
})
