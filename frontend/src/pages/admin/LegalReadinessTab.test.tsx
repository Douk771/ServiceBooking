import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { isLegalReadinessShape } from './legalReadinessHelpers'
import { LegalReadinessTab } from './LegalReadinessTab'
import type { LegalReadiness } from '../../types'

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

// `impact` is optional per contracts/cycle11/legal-status.schema.json (only the endpoint sends it;
// `legal status --json` shares this same schema but has no user base to compute it against). Before
// this fix, `LegalReadinessReport` read `data.impact.reAcceptanceRequired` unguarded and would have
// thrown, turning a partial report into a white screen instead of showing the rest of it.
const getReadiness = vi.fn()

vi.mock('../../api/platformSettings', () => ({
  adminLegalApi: {
    getReadiness: (...args: unknown[]) => getReadiness(...args),
  },
}))

function reportWithoutImpact(): Omit<LegalReadiness, 'impact'> {
  return {
    generatedAtUtc: '2026-09-01T00:00:00Z',
    root: '/legal',
    ready: true,
    blockers: [],
    documents: [],
    uiTexts: [],
    placeholders: [],
    links: { checked: 0, broken: [] },
    anchors: { missing: [] },
    disclaimer: 'Проверка механическая, не заменяет юридическую.',
  }
}

function renderTab() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <LegalReadinessTab />
    </QueryClientProvider>,
  )
}

describe('LegalReadinessTab — report without an `impact` block', () => {
  it('renders the rest of the report instead of crashing', async () => {
    getReadiness.mockResolvedValue(reportWithoutImpact())
    renderTab()
    expect(await screen.findByText('Комплект готов к публикации')).toBeInTheDocument()
    expect(screen.getByText('Проверка механическая, не заменяет юридическую.')).toBeInTheDocument()
    // The re-acceptance block itself is simply absent — no crash, no empty section header.
    expect(screen.queryByText('Кому потребуется повторный акцепт')).not.toBeInTheDocument()
  })
})
