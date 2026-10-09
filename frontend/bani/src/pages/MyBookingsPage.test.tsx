import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MyBookingsPage } from './MyBookingsPage'

const my = vi.hoisted(() => vi.fn())
vi.mock('../api/bathsOrders', () => ({ bathsOrdersApi: { my } }))

const item = (over = {}) => ({
  orderUrl: '/s/abc',
  companyName: 'Щербаны',
  resourceName: 'Баня по-чёрному',
  timeLabel: 'пт 15 янв, 22:00 — сб 16 янв, 01:00',
  localTimeNote: 'Время местное, Шерегеш',
  status: 'Confirmed',
  displayStatus: 'Confirmed',
  statusText: 'Подтверждена',
  totalRub: 6000,
  isActive: true,
  ...over,
})

function show() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <MyBookingsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}
beforeEach(() => my.mockReset())

describe('MyBookingsPage (bani)', () => {
  it('lists the bookings in the server order, each linking to its page', async () => {
    my.mockResolvedValue({ items: [item(), item({ orderUrl: '/s/old', resourceName: 'Чан', isActive: false, displayStatus: 'Completed', statusText: 'Состоялась' })] })
    show()
    const list = await screen.findByRole('list', { name: 'Список броней' })
    const links = list.querySelectorAll('a')
    expect([...links].map((a) => a.getAttribute('href'))).toEqual(['/s/abc', '/s/old'])
    expect(list).toHaveTextContent('Время местное, Шерегеш')
  })

  it('shows the empty state', async () => {
    my.mockResolvedValue({ items: [] })
    show()
    expect(await screen.findByText('Броней пока нет')).toBeInTheDocument()
  })

  it('shows the error with a retry', async () => {
    my.mockRejectedValueOnce(Object.assign(new Error('HTTP 500'), { isAxiosError: true, response: { status: 500, data: '', headers: {} } }))
    show()
    expect(await screen.findByRole('alert')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Повторить' })).toBeInTheDocument()
  })

  it('does not link an address that is not /s/<token>', async () => {
    my.mockResolvedValue({ items: [item({ orderUrl: 'https://evil.example/x' })] })
    show()
    await screen.findByText('Баня по-чёрному')
    expect(document.querySelector('a')).toBeNull()
  })
})
