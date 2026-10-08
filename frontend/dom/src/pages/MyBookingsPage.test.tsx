import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MyBookingsPage, bookingPathOf } from './MyBookingsPage'
import { httpError } from '../test/fixtures'

const my = vi.hoisted(() => vi.fn())
vi.mock('../api/guestBookings', () => ({ guestBookingsApi: { my } }))

const renderPage = () =>
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter>
        <MyBookingsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )

beforeEach(() => my.mockReset())

describe('MyBookingsPage (P1)', () => {
  it('lists bookings in the server order with the status in words and links to the booking page', async () => {
    my.mockResolvedValue([
      { bookingUrl: `${window.location.origin}/b/tok1`, houseName: 'Дом 1', companyName: 'Лесной', checkInDate: '2027-01-05', checkOutDate: '2027-01-08', status: 'Confirmed', displayStatus: 'Confirmed', statusText: 'Подтверждена', totalRub: 15000 },
      { bookingUrl: 'https://evil.example/b/x', houseName: 'Дом 2', companyName: 'Х', checkInDate: '2026-01-05', checkOutDate: '2026-01-08', status: 'ExpiredUnpaid', displayStatus: 'Completed', statusText: 'Завершена', totalRub: 9000 },
    ])
    renderPage()
    const link = await screen.findByRole('link', { name: /Дом 1/ })
    expect(link).toHaveAttribute('href', '/b/tok1')
    expect(screen.getByText('Подтверждена')).toBeInTheDocument()
    // A foreign address is shown but never followed.
    expect(screen.getByText('Дом 2')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Дом 2/ })).not.toBeInTheDocument()
  })

  it('empty, and failed with a retry', async () => {
    my.mockResolvedValueOnce([])
    const { unmount } = renderPage()
    expect(await screen.findByText('Броней пока нет')).toBeInTheDocument()
    unmount()
    my.mockReset().mockImplementation(() => Promise.reject(httpError(500, '')))
    renderPage()
    expect(await screen.findByRole('alert')).toHaveTextContent('Сервер временно недоступен')
  })

  it('follows only same-origin /b/ addresses', () => {
    expect(bookingPathOf(`${window.location.origin}/b/abc`)).toBe('/b/abc')
    expect(bookingPathOf(`${window.location.origin}/cabinet`)).toBeNull()
    expect(bookingPathOf('https://evil.example/b/abc')).toBeNull()
  })
})
