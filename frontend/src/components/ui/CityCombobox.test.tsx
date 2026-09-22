import { describe, it, expect, vi, beforeEach } from 'vitest'
import type { ReactElement } from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { CityCombobox } from './CityCombobox'
import type { City } from '../../types'

const search = vi.fn()

vi.mock('../../api/cities', () => ({
  citiesApi: {
    search: (...args: unknown[]) => search(...args),
  },
}))

const MOSCOW: City = {
  id: 1,
  name: 'Москва',
  region: 'Москва',
  timeZoneId: 'Europe/Moscow',
  utcOffsetMinutes: 180,
  label: 'Москва, Москва',
}

beforeEach(() => {
  search.mockReset().mockResolvedValue([MOSCOW])
})

function renderWithProviders(ui: ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>)
}

/**
 * ARCHITECTURE_CYCLE9.md §103.4 (US-114). Sending the full "{Name}, {Region}" label to the server on
 * focus produced zero matches and a false "город не найден" — the regression this guards against.
 */
describe('CityCombobox', () => {
  it('focusing a field with a selected city searches by name, not by the full label, and shows no "not found"', async () => {
    const user = userEvent.setup()
    renderWithProviders(<CityCombobox value={MOSCOW} onChange={vi.fn()} />)

    await user.click(screen.getByRole('combobox'))

    await waitFor(() => expect(search).toHaveBeenCalled())
    const [term] = search.mock.calls[search.mock.calls.length - 1] as [string]
    expect(term).toBe('Москва')
    expect(term).not.toContain(',')

    expect(await screen.findByRole('option', { name: /Москва/ })).toBeInTheDocument()
    expect(screen.queryByText('Город не найден')).not.toBeInTheDocument()
  })

  it('typing after a selection clears it and searches by the typed text (still no false "not found")', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    renderWithProviders(<CityCombobox value={MOSCOW} onChange={onChange} />)

    const input = screen.getByRole('combobox')
    await user.click(input)
    await waitFor(() => expect(search).toHaveBeenCalled())
    search.mockClear()

    await user.type(input, 'x')

    await waitFor(() => expect(search).toHaveBeenCalled())
    const [term] = search.mock.calls[search.mock.calls.length - 1] as [string]
    expect(term).toContain('x')
    expect(onChange).toHaveBeenCalledWith(null)
  })

  it('empty result while typing (dirty) shows "город не найден"', async () => {
    search.mockResolvedValue([])
    const user = userEvent.setup()
    renderWithProviders(<CityCombobox value={null} onChange={vi.fn()} />)

    await user.type(screen.getByRole('combobox'), 'zzz')

    expect(await screen.findByText('Город не найден')).toBeInTheDocument()
  })
})
