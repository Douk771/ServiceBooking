import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import { DemoBanner } from './DemoBanner'
import { neighbourDemoLink } from '../../utils/demoNeighbour'
import { DemoProductProvider } from './DemoProductContext'
import { SHOWCASE_FALLBACK_TEXTS } from '../../utils/showcaseTexts'
import { demoOrdersStatus, demoStatus, renderWithProviders } from './testUtils'

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

describe('DemoBanner: link to the other demo (US-35-10, API_CONTRACT_CYCLE35.md §35.21 siteUrls)', () => {
  beforeEach(() => {
    getText.mockRejectedValue(new Error('404'))
  })

  it('ezbook (services): «Посмотреть демо «Заказов»» leads to siteUrls.orders', async () => {
    getStatus.mockResolvedValue(demoStatus())
    renderWithProviders(<DemoBanner />)
    const link = await screen.findByRole('link', { name: 'Посмотреть демо «Заказов»' })
    expect(link).toHaveAttribute('href', 'https://demo.zakaz.ezbook.ru')
    expect(link.className).toContain('min-h-[44px]')
  })

  it('goods (orders): «Посмотреть демо «Записи»» leads to siteUrls.services', async () => {
    getStatus.mockResolvedValue(demoOrdersStatus())
    renderWithProviders(
      <DemoProductProvider product="orders">
        <DemoBanner />
      </DemoProductProvider>,
    )
    const link = await screen.findByRole('link', { name: 'Посмотреть демо «Записи»' })
    expect(link).toHaveAttribute('href', 'https://demo.visit.ezbook.ru')
  })

  it('the compact strip (embed widget) has no cross-link', async () => {
    getStatus.mockResolvedValue(demoStatus())
    renderWithProviders(<DemoBanner compact />)
    await screen.findByRole('note')
    expect(screen.queryByTestId('demo-neighbour-link')).toBeNull()
  })

  it('production: nothing at all, no link either', async () => {
    getStatus.mockResolvedValue(null)
    renderWithProviders(<DemoBanner />)
    await waitFor(() => expect(getStatus).toHaveBeenCalled())
    await Promise.resolve()
    expect(screen.queryByRole('link')).toBeNull()
    expect(screen.queryByTestId('demo-banner')).toBeNull()
  })

  it('a status without a usable address (older API): the banner stays, the link does not appear', async () => {
    getStatus.mockResolvedValue(demoStatus({ siteUrls: undefined as never }))
    renderWithProviders(<DemoBanner />)
    await screen.findByRole('note')
    expect(screen.queryByTestId('demo-neighbour-link')).toBeNull()
  })
})

describe('neighbourDemoLink', () => {
  const urls = { services: 'https://demo.visit.ezbook.ru', orders: 'https://demo.zakaz.ezbook.ru' }

  it('picks the other product and ignores a non-http address', () => {
    expect(neighbourDemoLink('services', urls)?.href).toBe(urls.orders)
    expect(neighbourDemoLink('orders', urls)?.href).toBe(urls.services)
    expect(neighbourDemoLink('services', { ...urls, orders: 'javascript:alert(1)' })).toBeNull()
    expect(neighbourDemoLink('services', { ...urls, orders: '  ' })).toBeNull()
    expect(neighbourDemoLink('orders', undefined)).toBeNull()
  })
})
