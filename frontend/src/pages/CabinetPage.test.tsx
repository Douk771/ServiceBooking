import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { MyCompaniesTab } from './CabinetPage'
import type { LegalManifest, LegalText } from '../types'

const getMy = vi.fn()
const create = vi.fn()
const getManifest = vi.fn()
const getText = vi.fn()
const confirmNotice = vi.fn()
const saveAddress = vi.fn()
const searchCities = vi.fn()

vi.mock('../api/companies', () => ({
  companiesApi: {
    getMy: (...args: unknown[]) => getMy(...args),
    create: (...args: unknown[]) => create(...args),
  },
}))
vi.mock('../api/legal', () => ({
  legalApi: {
    getManifest: (...args: unknown[]) => getManifest(...args),
    getText: (...args: unknown[]) => getText(...args),
  },
}))
vi.mock('../api/companyAddress', () => ({
  companyAddressApi: {
    confirmNotice: (...args: unknown[]) => confirmNotice(...args),
    saveAddress: (...args: unknown[]) => saveAddress(...args),
  },
}))
vi.mock('../api/cities', () => ({
  citiesApi: { search: (...args: unknown[]) => searchCities(...args) },
}))

const noticeHtml = '<h2>Текст для владельца</h2><p>Этот адрес увидит любой человек в интернете.</p><h2>Подтверждение</h2><p>x</p>'

function manifest(overrides: Partial<LegalManifest> = {}): LegalManifest {
  return {
    documents: [
      {
        type: 'TermsOwner',
        title: 'Соглашение с компанией',
        version: 'v1',
        effectiveFrom: '2026-01-01T00:00:00Z',
        isDraft: false,
        changeKind: 'Editorial',
        gate: 'None',
        url: '/terms-owner',
      },
    ],
    uiTexts: [],
    ...overrides,
  }
}

function noticeText(): LegalText {
  return { key: 'PublicAddressNotice', version: 'v1', isDraft: false, contentHtml: noticeHtml }
}

function renderTab() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <MyCompaniesTab />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

async function openFormAndFillRequired(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: '+ Создать компанию' }))
  await user.type(screen.getByLabelText('Название *'), 'Салон')

  searchCities.mockResolvedValue([{ id: 1, name: 'Барнаул', label: 'Барнаул, Алтайский край', utcOffsetMinutes: 240 }])
  const cityInput = screen.getByLabelText('Город *')
  await user.click(cityInput)
  await user.type(cityInput, 'Барн')
  const option = await screen.findByText('Барнаул, Алтайский край')
  await user.click(option)

  await user.click(screen.getByRole('checkbox', { name: /Я принимаю/ }))
}

beforeEach(() => {
  getMy.mockReset().mockResolvedValue([])
  create.mockReset()
  getManifest.mockReset()
  getText.mockReset().mockResolvedValue(noticeText())
  confirmNotice.mockReset().mockResolvedValue({ version: 'v1', acknowledgedAt: '2026-09-24T10:00:00Z' })
  saveAddress.mockReset()
  searchCities.mockReset()
})

describe('MyCompaniesTab — ownerTerms must be checked before the public-address-notice consent write (review finding, cycle 13)', () => {
  it('lets the owner submit through the address notice when ownerTerms loaded fine', async () => {
    getManifest.mockResolvedValue(manifest())
    create.mockResolvedValue({ company: { id: 'c1', address: '' }, token: 't' })
    const user = userEvent.setup()
    renderTab()

    await openFormAndFillRequired(user)
    await user.type(screen.getByLabelText('Адрес'), 'ул. Ленина 1')
    await user.click(screen.getByRole('button', { name: 'Создать' }))

    expect(await screen.findByRole('button', { name: 'Понятно, сохранить адрес' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Понятно, сохранить адрес' }))

    await waitFor(() => expect(create).toHaveBeenCalled())
  })

  it('shows an error and never opens the address notice (no consent-ledger write) when ownerTerms failed to load', async () => {
    // `GET /api/legal/documents` resolved without a TermsOwner entry — `ownerTerms` stays `undefined`.
    getManifest.mockResolvedValue(manifest({ documents: [] }))
    const user = userEvent.setup()
    renderTab()

    await openFormAndFillRequired(user)
    await user.type(screen.getByLabelText('Адрес'), 'ул. Ленина 1')

    // The submit button is disabled while `ownerTerms` is missing (`disabled={!ownerTermsAccepted ||
    // !ownerTerms}`), so the only way this handler runs in that state is a raw `submit` event on the
    // form itself — exactly the defence-in-depth case this check exists for (review finding, cycle 13:
    // the pre-gate check must stop the notice, and its consent-ledger write, before it, not after).
    const form = screen.getByRole('button', { name: 'Создать' }).closest('form')!
    fireEvent.submit(form)

    expect(
      await screen.findByText('Не удалось загрузить текст соглашения. Обновите страницу и попробуйте снова.'),
    ).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Понятно, сохранить адрес' })).not.toBeInTheDocument()
    expect(confirmNotice).not.toHaveBeenCalled()
    expect(create).not.toHaveBeenCalled()
  })
})
