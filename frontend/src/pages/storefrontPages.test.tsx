import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Navigate, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ContactsPage } from './ContactsPage'
import { PaymentInfoPage } from './PaymentInfoPage'
import { LegalGuard } from '@/components/legal/LegalGuard'
import { useAuthStore } from '@/store/authStore'
import { useLegalStore } from '@/store/legalStore'
import { OPERATOR, type OperatorKey, type StorefrontSite } from '@/utils/operatorRequisites'
import { STOREFRONT_PAGES } from '@/utils/storefrontMarkup'
import appSource from '../App.tsx?raw'

// CY48-03 / CY48-04 (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.13.1): /contacts and /payment-info render fully without any network,
// on all four sites, and stay visible under a pending consent gate.
vi.mock('@/api/legal', () => ({
  legalApi: {
    getConsentStatus: vi.fn().mockResolvedValue({ requiresAcceptance: true, showBanner: false, documents: [] }),
  },
}))

// API_CONTRACT_CYCLE48 §48.21 «Где на сайте»: the other keys are on /payment-info (НДС, сроки счёта и активации) and in the footer (город).
const CONTACTS_KEYS: OperatorKey[] = [
  'НАИМЕНОВАНИЕ_ОПЕРАТОРА',
  'ИНН_ОПЕРАТОРА',
  'ЮРИДИЧЕСКИЙ_АДРЕС',
  'ПОЧТОВЫЙ_АДРЕС',
  'ПОЧТА_ДЛЯ_ОБРАЩЕНИЙ',
  'ТЕЛЕФОН_ОПЕРАТОРА',
  'СРОК_ОТВЕТА_НА_ОБРАЩЕНИЕ',
  'ПОЧТА_ПОДДЕРЖКИ',
  'РЕЖИМ_РАБОТЫ',
]
const SITES: StorefrontSite[] = ['zapis', 'goods', 'dom', 'bani']

let fetchSpy: ReturnType<typeof vi.fn>
let xhrOpen: ReturnType<typeof vi.spyOn>
beforeEach(() => {
  vi.stubEnv('DEV', false)
  // Any request fails the test: the pages are static.
  fetchSpy = vi.fn(() => {
    throw new Error('network call from a static page')
  })
  vi.stubGlobal('fetch', fetchSpy)
  xhrOpen = vi.spyOn(XMLHttpRequest.prototype, 'open').mockImplementation(() => {
    throw new Error('network call from a static page')
  })
})
afterEach(() => {
  vi.unstubAllEnvs()
  vi.unstubAllGlobals()
  xhrOpen.mockRestore()
  useAuthStore.setState({ token: null })
  useLegalStore.setState({ consentRequired: false })
})

const noNetwork = () => {
  expect(fetchSpy).not.toHaveBeenCalled()
  expect(xhrOpen).not.toHaveBeenCalled()
}

describe.each(SITES)('ContactsPage on %s', (site) => {
  it('one h1, the title, every requisite value, no unfilled placeholder, no network', () => {
    const { container } = render(
      <MemoryRouter>
        <ContactsPage site={site} />
      </MemoryRouter>,
    )
    expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1)
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Контакты и реквизиты')
    expect(document.title).toBe(STOREFRONT_PAGES.pages.contacts.title)
    expect(container.querySelector('main')).not.toBeNull()
    const text = container.textContent ?? ''
    for (const key of CONTACTS_KEYS) expect(text, key).toContain(OPERATOR[key])
    expect(text).not.toContain('{{')
    noNetwork()
  })

  it('«Тарифы и цены» is listed only where /pricing exists (ezbook, goods)', () => {
    render(
      <MemoryRouter>
        <ContactsPage site={site} />
      </MemoryRouter>,
    )
    const link = screen.queryByRole('link', { name: 'Тарифы и цены' })
    if (site === 'zapis' || site === 'goods') expect(link).toHaveAttribute('href', '/pricing')
    else expect(link).toBeNull()
    expect(screen.getByRole('link', { name: 'Оплата и возврат' })).toHaveAttribute('href', '/payment-info')
  })
})

describe.each(SITES)('PaymentInfoPage on %s', (site) => {
  it('one h1, the title, the filled terms, no unfilled placeholder, no network', () => {
    const { container } = render(
      <MemoryRouter>
        <PaymentInfoPage site={site} />
      </MemoryRouter>,
    )
    expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1)
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Оплата и возврат')
    expect(document.title).toBe(STOREFRONT_PAGES.pages.paymentInfo.title)
    const text = container.textContent ?? ''
    expect(text).toContain(OPERATOR['ИНН_ОПЕРАТОРА'])
    expect(text).toContain(OPERATOR['НДС_ОГОВОРКА'])
    expect(text).toContain(OPERATOR['СРОК_СЧЁТА'])
    expect(text).toContain(OPERATOR['СРОК_АКТИВАЦИИ'])
    expect(text).not.toContain('{{')
    noNetwork()
  })

  it('«Тарифы и цены» leads to the «Записи» site from dom and bani, stays local on ezbook and goods', () => {
    render(
      <MemoryRouter>
        <PaymentInfoPage site={site} />
      </MemoryRouter>,
    )
    const links = screen.getAllByRole('link', { name: /Тарифы и цены/ })
    expect(links.length).toBeGreaterThan(0)
    const expected = site === 'dom' || site === 'bani' ? 'https://ezbook.ru/pricing' : '/pricing'
    for (const a of links) expect(a).toHaveAttribute('href', expected)
  })
})

describe('consent gate bypass', () => {
  it.each([
    ['/contacts', 'Контакты и реквизиты'],
    ['/payment-info', 'Оплата и возврат'],
  ])('%s stays visible while a Material change waits for acceptance', async (path, heading) => {
    // The bypass list is the one App.tsx really passes to LegalGuard.
    const list = appSource.match(/CONSENT_GATE_BYPASS_PATHS = \[([\s\S]*?)\]/)![1]
    const bypassPaths = [...list.matchAll(/'([^']+)'/g)].map((m) => m[1])
    useAuthStore.setState({ token: 't' })
    useLegalStore.setState({ consentRequired: true })
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={[path]}>
          <LegalGuard bypassPaths={bypassPaths}>
            <Routes>
              <Route path="/contacts" element={<ContactsPage site="zapis" />} />
              <Route path="/payment-info" element={<PaymentInfoPage site="zapis" />} />
            </Routes>
          </LegalGuard>
        </MemoryRouter>
      </QueryClientProvider>,
    )
    await waitFor(() => expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(heading))
  })
})

describe('/payment-terms', () => {
  it('redirects to /payment-info', () => {
    render(
      <MemoryRouter initialEntries={['/payment-terms']}>
        <Routes>
          <Route path="/payment-terms" element={<Navigate to="/payment-info" replace />} />
          <Route path="/payment-info" element={<PaymentInfoPage site="goods" />} />
        </Routes>
      </MemoryRouter>,
    )
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Оплата и возврат')
  })
})
