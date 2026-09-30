import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { SalonCatalogListingSection } from './SalonCatalogListingSection'
import type { SalonCatalogListingDto } from '../../types'

const get = vi.fn()
const put = vi.fn()
vi.mock('../../api/companyCatalogListing', () => ({
  companyCatalogListingApi: { get: (...a: unknown[]) => get(...a), put: (...a: unknown[]) => put(...a) },
}))

const dto = (over: Partial<SalonCatalogListingDto> = {}): SalonCatalogListingDto =>
  ({
    showInCatalog: false,
    allowedByPlan: true,
    visible: false,
    statusText: 'Салона сейчас нет в каталоге',
    notAllowedByPlanText: null,
    checklist: [{ code: 'HiddenByOwner', text: 'Показ включен в настройках', done: false }],
    ...over,
  }) as SalonCatalogListingDto

const renderIt = () =>
  render(
    <QueryClientProvider client={new QueryClient()}>
      <SalonCatalogListingSection companyId="co1" />
    </QueryClientProvider>,
  )

beforeEach(() => {
  get.mockReset()
  put.mockReset()
})

describe('SalonCatalogListingSection', () => {
  it('shows the title, the switch label and the server status', async () => {
    get.mockResolvedValue(dto())
    renderIt()
    expect(await screen.findByRole('switch', { name: 'Показывать салон в каталоге ezbook.ru' })).not.toBeChecked()
    expect(screen.getByRole('heading', { name: 'Каталог ezbook.ru' })).toBeInTheDocument()
    expect(screen.getByTestId('listing-status')).toHaveTextContent('Салона сейчас нет в каталоге')
  })

  it('a click saves immediately with put(companyId, true) and shows the answer', async () => {
    get.mockResolvedValue(dto())
    put.mockResolvedValue(dto({ showInCatalog: true, visible: true, statusText: 'Салон виден в каталоге ezbook.ru' }))
    renderIt()
    await userEvent.click(await screen.findByRole('switch'))
    await waitFor(() => expect(put).toHaveBeenCalledWith('co1', true))
    await waitFor(() => expect(screen.getByTestId('listing-status')).toHaveTextContent('Салон виден'))
    expect(screen.getByRole('switch')).toBeChecked()
  })

  it('a 409 JSON shows the server message and the switch returns to the server value', async () => {
    get.mockResolvedValue(dto())
    put.mockRejectedValue({ response: { status: 409, data: { code: 'CatalogListingNotAllowedByPlan', message: 'Показ в каталоге не входит в ваш тариф' } } })
    renderIt()
    await userEvent.click(await screen.findByRole('switch'))
    expect(await screen.findByRole('alert')).toHaveTextContent('Показ в каталоге не входит в ваш тариф')
    await waitFor(() => expect(get).toHaveBeenCalledTimes(2)) // refetched after the error
    expect(screen.getByRole('switch')).not.toBeChecked()
  })

  it('a 404 on load shows "Компания не найдена" with a retry', async () => {
    get.mockRejectedValue({ response: { status: 404, data: '' } })
    renderIt()
    expect(await screen.findByText('Компания не найдена')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Повторить' })).toBeInTheDocument()
  })

  it('a network failure on load shows the fallback text', async () => {
    get.mockRejectedValue(new Error('Network Error'))
    renderIt()
    expect(await screen.findByText('Не удалось загрузить настройку каталога.')).toBeInTheDocument()
  })
})
