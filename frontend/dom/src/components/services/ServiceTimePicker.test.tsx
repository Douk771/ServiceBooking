import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ServiceTimePicker } from './ServiceTimePicker'
import type { ServiceAvailabilityDto, ServiceStartsDto } from '../../types'
import type { SessionPick } from '../../utils/serviceSelection'

const availability: ServiceAvailabilityDto = {
  serviceId: 's1',
  today: '2027-01-14',
  days: [
    { businessDate: '2027-01-14', label: 'чт 14 янв', hasStarts: false },
    { businessDate: '2027-01-15', label: 'пт 15 янв', hasStarts: true },
  ],
}
const starts: ServiceStartsDto = {
  serviceId: 's1',
  businessDate: '2027-01-15',
  dateLabel: 'пт 15 янв',
  minHours: 2,
  maxHours: 4,
  starts: [
    { startMinute: 1320, startUtc: '2027-01-15T15:00:00Z', label: '22:00', maxHours: 4, options: [{ hours: 2, endLabel: 'сб 16 янв, 00:00' }, { hours: 3, endLabel: 'сб 16 янв, 01:00' }] },
    { startMinute: 1470, startUtc: '2027-01-15T17:30:00Z', label: '00:30 (ночь на сб)', maxHours: 2, options: [{ hours: 2, endLabel: '02:30' }] },
  ],
}
const items = [{ id: 'i1', name: 'Веник берёзовый', priceRub: 300, maxPerSession: 3 }]

function setup() {
  const onChange = vi.fn<(p: SessionPick | null) => void>()
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <ServiceTimePicker scope="s1" items={items} loadAvailability={() => Promise.resolve(availability)} loadStarts={() => Promise.resolve(starts)} onChange={onChange} />
    </QueryClientProvider>,
  )
  return onChange
}

describe('ServiceTimePicker', () => {
  it('a date without free time is disabled and says so in words, not only in colour', async () => {
    setup()
    const closed = await screen.findByRole('button', { name: /чт 14 янв/ })
    expect(closed).toBeDisabled()
    expect(closed).toHaveTextContent('нет свободного времени')
    expect(screen.getByRole('button', { name: /пт 15 янв/ })).toBeEnabled()
  })

  it('walks date → start → hours → positions; starts after midnight form their own group; the positions start at 0', async () => {
    const onChange = setup()
    fireEvent.click(await screen.findByRole('button', { name: /пт 15 янв/ }))
    const night = await screen.findByText('После полуночи')
    expect(within(night.parentElement as HTMLElement).getByRole('button', { name: /00:30 \(ночь на сб\)/ })).toBeInTheDocument()

    expect(onChange).toHaveBeenLastCalledWith(null)
    fireEvent.click(screen.getByRole('button', { name: /Начало в 22:00/ }))
    fireEvent.click(await screen.findByRole('button', { name: /^3 ч/ }))
    expect(screen.getByRole('button', { name: /^3 ч/ })).toHaveTextContent('до сб 16 янв, 01:00')
    expect(screen.getByText('Веник берёзовый')).toBeInTheDocument()
    expect(screen.getByLabelText('Веник берёзовый: 0')).toBeInTheDocument()
    await waitFor(() => expect(onChange).toHaveBeenLastCalledWith({ businessDate: '2027-01-15', startMinute: 1320, hours: 3, quantities: {} }))

    fireEvent.click(screen.getByRole('button', { name: 'Веник берёзовый: больше' }))
    await waitFor(() => expect(onChange).toHaveBeenLastCalledWith({ businessDate: '2027-01-15', startMinute: 1320, hours: 3, quantities: { i1: 1 } }))
  })

  it('changing the date drops the chosen start and hours', async () => {
    const onChange = setup()
    fireEvent.click(await screen.findByRole('button', { name: /пт 15 янв/ }))
    fireEvent.click(await screen.findByRole('button', { name: /Начало в 22:00/ }))
    fireEvent.click(await screen.findByRole('button', { name: /^2 ч/ }))
    await waitFor(() => expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({ hours: 2 })))
    fireEvent.click(screen.getByRole('button', { name: /пт 15 янв/ }))
    await waitFor(() => expect(onChange).toHaveBeenLastCalledWith(null))
  })
})
