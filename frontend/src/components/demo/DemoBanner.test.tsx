import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import { DemoBanner } from './DemoBanner'
import { SHOWCASE_FALLBACK_TEXTS } from '../../utils/showcaseTexts'
import { demoStatus, renderWithProviders } from './testUtils'

const getStatus = vi.fn()
const getText = vi.fn()
vi.mock('../../api/demo', () => ({ demoApi: { getStatus: (...a: unknown[]) => getStatus(...a) } }))
vi.mock('../../api/legal', () => ({ legalApi: { getText: (...a: unknown[]) => getText(...a) } }))

beforeEach(() => {
  getStatus.mockReset()
  getText.mockReset()
})

describe('DemoBanner (API_CONTRACT_CYCLE28.md §600, L28-3)', () => {
  it('production (status 404 → null): nothing is rendered and the text is not even requested', async () => {
    getStatus.mockResolvedValue(null)
    renderWithProviders(<DemoBanner />)
    await waitFor(() => expect(getStatus).toHaveBeenCalled())
    await Promise.resolve()
    expect(screen.queryByTestId('demo-banner')).toBeNull()
    expect(getText).not.toHaveBeenCalled()
  })

  it('status request failed: no banner (and no crash)', async () => {
    getStatus.mockRejectedValue(new Error('network'))
    renderWithProviders(<DemoBanner />)
    await waitFor(() => expect(getStatus).toHaveBeenCalled())
    expect(screen.queryByTestId('demo-banner')).toBeNull()
  })

  it('demo + text not published (404): the verbatim fallback as role=note', async () => {
    getStatus.mockResolvedValue(demoStatus())
    getText.mockRejectedValue(new Error('404'))
    renderWithProviders(<DemoBanner />)
    const banner = await screen.findByRole('note')
    expect(banner).toHaveTextContent(SHOWCASE_FALLBACK_TEXTS.DemoBanner)
  })

  it('demo + published text: the live text wins', async () => {
    getStatus.mockResolvedValue(demoStatus())
    getText.mockResolvedValue({
      key: 'DemoBanner',
      version: 'v1',
      isDraft: false,
      contentHtml: '<p>Это демо-стенд EZBOOK.</p>',
    })
    renderWithProviders(<DemoBanner compact />)
    await waitFor(() => expect(screen.getByTestId('demo-banner')).toHaveTextContent('Это демо-стенд EZBOOK.'))
  })
})
