import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { CatalogListingSection } from './CatalogListingSection'
import type { CatalogListingDto } from '../types'

const get = vi.fn()
const put = vi.fn()
vi.mock('../api/catalogListing', () => ({ catalogListingApi: { get: (...a: unknown[]) => get(...a), put: (...a: unknown[]) => put(...a) } }))

const dto = (over: Partial<CatalogListingDto> = {}): CatalogListingDto => ({
  showInCatalog: true, allowedByPlan: true, visible: false, statusText: 'Магазина сейчас нет в каталоге', notAllowedByPlanText: null,
  checklist: [{ code: 'NoWorkingHours', text: 'Задайте часы работы', done: false }, { code: 'NoPublishedProducts', text: 'Опубликуйте хотя бы один товар', done: true }], ...over,
})
const renderIt = () => render(<QueryClientProvider client={new QueryClient()}><CatalogListingSection shopId="s1" /></QueryClientProvider>)

beforeEach(() => { get.mockReset(); put.mockReset() })

describe('CatalogListingSection', () => {
  it('prints the server status and checklist as text', async () => {
    get.mockResolvedValue(dto())
    renderIt()
    expect(await screen.findByTestId('listing-status')).toHaveTextContent('Магазина сейчас нет в каталоге')
    const checks = screen.getAllByTestId('listing-check')
    expect(checks).toHaveLength(2)
    expect(checks[0]).toHaveAttribute('data-done', 'false')
    expect(checks[0]).toHaveTextContent('не выполнено')
  })

  it('disables the switch and explains it when the plan does not allow listing', async () => {
    get.mockResolvedValue(dto({ showInCatalog: false, allowedByPlan: false, notAllowedByPlanText: 'Показ в каталоге не входит в ваш тариф' }))
    renderIt()
    expect(await screen.findByRole('switch')).toBeDisabled()
    expect(screen.getByTestId('listing-not-allowed')).toHaveTextContent('не входит в ваш тариф')
  })

  it('saves the switch and shows the answer', async () => {
    get.mockResolvedValue(dto())
    put.mockResolvedValue(dto({ showInCatalog: false, statusText: 'Магазина сейчас нет в каталоге' }))
    const user = userEvent.setup()
    renderIt()
    await user.click(await screen.findByRole('switch'))
    await waitFor(() => expect(put).toHaveBeenCalledWith('s1', false))
  })

  it('shows an error with a retry when the load fails', async () => {
    get.mockRejectedValue({ response: { status: 500, data: '' } })
    renderIt()
    expect(await screen.findByRole('alert')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Повторить' })).toBeInTheDocument()
  })
})
