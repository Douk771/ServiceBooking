import { describe, it, expect } from 'vitest'
import { isLegalReadinessShape } from './LegalReadinessTab'

// A 503 that never went through AdminLegalController (reverse proxy/gateway health page, plain
// text, etc.) must be rejected rather than cast blindly — see review finding on
// LegalReadinessTab.tsx: an untyped truthy body would otherwise crash the report on
// `data.placeholders.reduce`.
describe('isLegalReadinessShape', () => {
  it('accepts a well-formed readiness report', () => {
    expect(
      isLegalReadinessShape({
        ready: false,
        blockers: [{ kind: 'LegalUnavailable', detail: 'x' }],
        placeholders: [],
      }),
    ).toBe(true)
  })

  it('rejects an HTML string body from a gateway 503', () => {
    expect(isLegalReadinessShape('<html><body>503 Service Unavailable</body></html>')).toBe(false)
  })

  it('rejects undefined/null', () => {
    expect(isLegalReadinessShape(undefined)).toBe(false)
    expect(isLegalReadinessShape(null)).toBe(false)
  })

  it('rejects an object missing required arrays', () => {
    expect(isLegalReadinessShape({ ready: false })).toBe(false)
    expect(isLegalReadinessShape({ blockers: [], placeholders: [] })).toBe(false)
  })

  it('rejects a plain empty object', () => {
    expect(isLegalReadinessShape({})).toBe(false)
  })
})
