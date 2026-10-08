import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen, fireEvent, waitFor, within } from '@testing-library/react'
import { BookingCardPage } from './BookingCardPage'
import { companyFixture, httpError, staffCardFixture } from '../../test/fixtures'
import { renderCabinetPage } from '../../test/renderCabinet'

const api = vi.hoisted(() => ({ booking: vi.fn(), confirmPayment: vi.fn(), rejectPayment: vi.fn(), cancel: vi.fn(), proofBlob: vi.fn() }))
vi.mock('../../api/staysBoard', () => ({ staysBoardApi: api }))
vi.mock('../../api/legalTexts', () => ({ stayLegalTextsApi: { get: () => Promise.reject(httpError(404, '')) } }))

const open = (company = companyFixture()) => renderCabinetPage(<BookingCardPage />, { company, path: '/cabinet/:companyId/bookings/:bookingId', url: '/cabinet/co-1/bookings/bk-1' })

beforeEach(() => {
  Object.values(api).forEach((f) => f.mockReset())
  api.booking.mockResolvedValue(staffCardFixture())
})

describe('BookingCardPage — actions with expectedVersion', () => {
  it('shows the guest, the stay, the sum, the proof and the journal', async () => {
    open()
    expect(await screen.findByRole('heading', { name: 'Анна Петрова' })).toBeInTheDocument()
    expect(screen.getByText('Ожидает проверки оплаты')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '+7 (900) 123-45-67' })).toHaveAttribute('href', 'tel:+79001234567')
    expect(screen.getByText('Предоплата 30 %')).toBeInTheDocument()
    expect(screen.getByText(/Файл 1/)).toBeInTheDocument()
    expect(screen.getByTestId('journal')).toHaveTextContent('Бронь создана гостем')
  })

  it('confirming sends the version of the card the owner is looking at, and the answer replaces the card', async () => {
    api.confirmPayment.mockResolvedValue(staffCardFixture({ version: 4, status: 'Confirmed', displayStatus: 'Confirmed', statusText: 'Подтверждена', availableActions: ['Cancel'], paymentConfirmed: { atUtc: '2027-01-02T11:00:00Z', byName: 'Иван' } }))
    open()
    fireEvent.click(await screen.findByRole('button', { name: 'Подтвердить оплату' }))
    await waitFor(() => expect(api.confirmPayment).toHaveBeenCalledWith('co-1', 'bk-1', 3))
    expect(await screen.findByText('Подтверждена')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Подтвердить оплату' })).not.toBeInTheDocument()
    expect(screen.getByText(/Оплату подтвердил Иван/)).toBeInTheDocument()
  })

  it('rejecting needs a reason (the guest will read it); the version goes with it', async () => {
    api.rejectPayment.mockResolvedValue(staffCardFixture({ version: 4, status: 'PaymentRejected', displayStatus: 'PaymentRejected', statusText: 'Оплата не подтверждена', availableActions: [], statusReason: 'Платёж не поступил' }))
    open()
    fireEvent.click(await screen.findByRole('button', { name: 'Отклонить оплату' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(/Если деньги всё же пришли, верните их или восстановите бронь/)).toBeInTheDocument()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Отклонить оплату' }))
    expect(await within(dialog).findByText('Укажите причину — гость её увидит')).toBeInTheDocument()
    expect(api.rejectPayment).not.toHaveBeenCalled()
    fireEvent.change(within(dialog).getByLabelText(/Причина/), { target: { value: '  Платёж не поступил ' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Отклонить оплату' }))
    await waitFor(() => expect(api.rejectPayment).toHaveBeenCalledWith('co-1', 'bk-1', 3, 'Платёж не поступил'))
    expect(await screen.findByText('Платёж не поступил')).toBeInTheDocument()
  })

  it('cancelling shows the full refund the guest is owed and asks for a reason', async () => {
    api.cancel.mockResolvedValue(staffCardFixture({ version: 4, status: 'CancelledByOwner', displayStatus: 'CancelledByOwner', statusText: 'Отменена компанией', availableActions: [] }))
    open()
    fireEvent.click(await screen.findByRole('button', { name: 'Отменить бронь' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByTestId('owner-refund-text')).toHaveTextContent('Гостю нужно вернуть предоплату полностью: 4 500 ₽')
    expect(within(dialog).getByText(/не отменяйте бронь раньше расчётного часа следующего дня/)).toBeInTheDocument()
    fireEvent.change(within(dialog).getByLabelText(/Причина/), { target: { value: 'Авария отопления' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Отменить бронь' }))
    await waitFor(() => expect(api.cancel).toHaveBeenCalledWith('co-1', 'bk-1', 3, 'Авария отопления'))
  })

  it('somebody changed the booking first (409 VersionMismatch): the action is NOT applied, the current card replaces the screen, the owner is told', async () => {
    const current = staffCardFixture({ version: 5, status: 'Confirmed', displayStatus: 'Confirmed', statusText: 'Подтверждена', availableActions: ['Cancel'] })
    api.confirmPayment.mockRejectedValue(httpError(409, { code: 'VersionMismatch', message: 'Бронь уже изменена — проверьте актуальное состояние', booking: current }))
    open()
    fireEvent.click(await screen.findByRole('button', { name: 'Подтвердить оплату' }))
    expect(await screen.findByTestId('card-banner')).toHaveTextContent('Бронь уже изменена — проверьте актуальное состояние')
    expect(screen.getByText('Подтверждена')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Подтвердить оплату' })).not.toBeInTheDocument()
    expect(api.confirmPayment).toHaveBeenCalledTimes(1) // no silent retry
  })

  it('an invalid transition (409) is handled the same way', async () => {
    api.confirmPayment.mockRejectedValue(httpError(409, { code: 'InvalidTransition', message: 'Действие недоступно в статусе «Отменена гостем»', booking: staffCardFixture({ version: 6, status: 'CancelledByGuest', displayStatus: 'CancelledByGuest', statusText: 'Отменена гостем', availableActions: [] }) }))
    open()
    fireEvent.click(await screen.findByRole('button', { name: 'Подтвердить оплату' }))
    expect(await screen.findByTestId('card-banner')).toHaveTextContent('Действие недоступно в статусе «Отменена гостем»')
    expect(screen.queryByTestId('card-actions')).not.toBeInTheDocument()
  })

  it('a manager without ManageBookings sees the card but no buttons', async () => {
    open(companyFixture({ myRole: 'Manager', myPermissions: ['ViewBookings', 'ViewSchedule', 'ViewCabinet'] }))
    await screen.findByRole('heading', { name: 'Анна Петрова' })
    expect(screen.queryByTestId('card-actions')).not.toBeInTheDocument()
  })

  it('opens an image proof through the API (a blob), never through a link', async () => {
    api.proofBlob.mockResolvedValue(new Blob([new Uint8Array(4)], { type: 'image/png' }))
    const create = vi.fn(() => 'blob:proof')
    Object.assign(URL, { createObjectURL: create, revokeObjectURL: vi.fn() })
    open()
    fireEvent.click(await screen.findByRole('button', { name: 'Посмотреть' }))
    await waitFor(() => expect(api.proofBlob).toHaveBeenCalledWith('co-1', 'bk-1', 'pr1'))
    expect(await screen.findByRole('img', { name: /Файл 1/ })).toHaveAttribute('src', 'blob:proof')
    expect(document.querySelector('a[href*="payment-proofs"]')).toBeNull()
  })

  it('a purged proof is said to be deleted by the retention rule', async () => {
    api.booking.mockResolvedValue(staffCardFixture({ paymentProofs: [{ id: 'pr1', contentType: 'image/png', sizeBytes: 0, uploadedAtUtc: '2027-01-02T10:05:00Z', purged: true }], paymentProofsPurgedAtUtc: '2027-04-01T00:00:00Z' }))
    open()
    expect(await screen.findByText(/файл удалён по сроку хранения/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Посмотреть' })).not.toBeInTheDocument()
  })

  it('an unknown booking is a not-found page; a failed load offers a retry', async () => {
    api.booking.mockRejectedValueOnce(httpError(404, ''))
    const { unmount } = open()
    expect(await screen.findByText('Бронь не найдена')).toBeInTheDocument()
    unmount()
    api.booking.mockReset().mockRejectedValueOnce(httpError(500, '')).mockResolvedValueOnce(staffCardFixture())
    open()
    expect(await screen.findByRole('alert')).toHaveTextContent('Сервер временно недоступен')
    fireEvent.click(screen.getByRole('button', { name: 'Повторить' }))
    expect(await screen.findByRole('heading', { name: 'Анна Петрова' })).toBeInTheDocument()
  })
})
