import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BookingPage } from './BookingPage'
import { bookingFixture, httpError } from '../test/fixtures'
import type { PublicStayBookingDto } from '../types'

const api = vi.hoisted(() => ({ get: vi.fn(), uploadProof: vi.fn(), proofBlob: vi.fn(), cancel: vi.fn(), pushSubscribe: vi.fn(), pushUnsubscribe: vi.fn(), my: vi.fn() }))
vi.mock('../api/guestBookings', () => ({ guestBookingsApi: api }))
vi.mock('../api/legalTexts', () => ({ stayLegalTextsApi: { get: () => Promise.reject(httpError(404, '')) } }))

function renderPage(token = 'tok') {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[`/b/${token}`]}>
        <Routes>
          <Route path="/b/:token" element={<BookingPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return qc
}

beforeEach(() => {
  Object.values(api).forEach((f) => f.mockReset())
})
afterEach(() => vi.useRealTimers())

describe('BookingPage — held booking', () => {
  it('shows the status text from the server, the hold timer on the server clock, and the payment details with «Скопировать»', async () => {
    api.get.mockResolvedValue(bookingFixture())
    renderPage()
    expect(await screen.findByTestId('booking-status')).toHaveTextContent('Удержана — ожидает оплаты')
    expect(screen.getByRole('timer')).toBeInTheDocument()
    const payment = screen.getByTestId('payment-details')
    expect(within(payment).getByText(/Сбербанк, карта 2202/)).toBeInTheDocument()
    expect(within(payment).getByText('Бронь дома')).toBeInTheDocument()
    expect(within(payment).getAllByRole('button', { name: /Скопировать/ })).toHaveLength(3)
    expect(screen.getByText('Приложить подтверждение оплаты')).toBeInTheDocument()
  })

  it('the guest-facing text says «подтверждение оплаты» and never «чек»', async () => {
    api.get.mockResolvedValue(bookingFixture())
    renderPage()
    await screen.findByTestId('booking-status')
    expect(document.body.textContent ?? '').not.toMatch(/(^|[^а-яё])чек/i)
  })

  it('copies the requisites', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })
    api.get.mockResolvedValue(bookingFixture())
    renderPage()
    const payment = await screen.findByTestId('payment-details')
    fireEvent.click(within(payment).getByRole('button', { name: 'Скопировать: реквизиты' }))
    await waitFor(() => expect(writeText).toHaveBeenCalledWith('Сбербанк, карта 2202 0000 0000 0000'))
    expect(await within(payment).findByText('Скопировано')).toBeInTheDocument()
  })

  it('an unknown token is a plain not-found page', async () => {
    api.get.mockRejectedValue(httpError(404, ''))
    renderPage('nope')
    expect(await screen.findByText('Бронь не найдена')).toBeInTheDocument()
  })

  it('a failed load offers a retry', async () => {
    api.get.mockRejectedValueOnce(httpError(500, '')).mockResolvedValueOnce(bookingFixture())
    renderPage()
    expect(await screen.findByRole('alert')).toHaveTextContent('Сервер временно недоступен')
    fireEvent.click(screen.getByRole('button', { name: 'Повторить' }))
    expect(await screen.findByTestId('booking-status')).toBeInTheDocument()
  })
})

describe('BookingPage — payment proofs', () => {
  it('the first file moves the booking to «ожидает проверки оплаты»: the screen takes the server answer', async () => {
    api.get.mockResolvedValue(bookingFixture())
    const after: PublicStayBookingDto = bookingFixture({
      status: 'AwaitingPaymentCheck',
      displayStatus: 'AwaitingPaymentCheck',
      statusText: 'Ожидает проверки оплаты',
      holdExpiresAtUtc: null,
      paymentProofs: [{ id: 'p1', contentType: 'image/png', sizeBytes: 2048, uploadedAtUtc: '2027-01-02T10:05:00Z', purged: false }],
    })
    api.uploadProof.mockResolvedValue(after)
    renderPage()
    await screen.findByTestId('booking-status')
    const input = document.getElementById('proof-input') as HTMLInputElement
    const file = new File([new Uint8Array(2048)], 'receipt.png', { type: 'image/png' })
    fireEvent.change(input, { target: { files: [file] } })
    await waitFor(() => expect(screen.getByTestId('booking-status')).toHaveTextContent('Ожидает проверки оплаты'))
    expect(api.uploadProof).toHaveBeenCalledTimes(1)
    expect(api.uploadProof.mock.calls[0][0]).toBe('tok')
    expect(screen.queryByRole('timer')).not.toBeInTheDocument()
    expect(screen.getByText(/компания проверяет перевод/)).toBeInTheDocument()
    expect(screen.getByText(/Файл 1/)).toBeInTheDocument()
  })

  it('a file over 10 MB is refused before sending, with the server wording', async () => {
    api.get.mockResolvedValue(bookingFixture())
    renderPage()
    await screen.findByTestId('booking-status')
    const big = new File([new Uint8Array(1)], 'big.pdf', { type: 'application/pdf' })
    Object.defineProperty(big, 'size', { value: 11 * 1024 * 1024 })
    fireEvent.change(document.getElementById('proof-input') as HTMLInputElement, { target: { files: [big] } })
    expect(await screen.findByText('Файл больше 10 МБ')).toBeInTheDocument()
    expect(api.uploadProof).not.toHaveBeenCalled()
  })

  it('a hold that expired meanwhile (409 HoldExpired) replaces the screen with the current booking and says what to do', async () => {
    api.get.mockResolvedValue(bookingFixture())
    const expired = bookingFixture({
      status: 'ExpiredUnpaid',
      displayStatus: 'ExpiredUnpaid',
      statusText: 'Снята: не оплачена',
      holdExpiresAtUtc: null,
      payment: null,
      proofs: { canAttach: false, maxCount: 3, maxBytes: 1, acceptedTypes: [] },
      outcomeText: 'Время на оплату истекло, бронь снята. Если вы успели оплатить — свяжитесь с компанией.',
    })
    api.uploadProof.mockRejectedValue(
      httpError(409, { code: 'HoldExpired', message: 'Время на оплату истекло, бронь снята. Если вы уже оплатили — свяжитесь с компанией: +7 900 123-45-67', booking: expired }),
    )
    renderPage()
    await screen.findByTestId('booking-status')
    fireEvent.change(document.getElementById('proof-input') as HTMLInputElement, { target: { files: [new File([new Uint8Array(10)], 'a.png', { type: 'image/png' })] } })
    await waitFor(() => expect(screen.getByTestId('booking-status')).toHaveTextContent('Снята: не оплачена'))
    expect(screen.getByTestId('booking-outcome')).toHaveTextContent('Время на оплату истекло')
    expect(screen.getByTestId('proof-refusal')).toHaveTextContent('Если вы уже оплатили — свяжитесь с компанией')
    expect(screen.queryByTestId('payment-details')).not.toBeInTheDocument()
  })
})

describe('BookingPage — cancel', () => {
  const paid = () =>
    bookingFixture({
      status: 'Confirmed',
      displayStatus: 'Confirmed',
      statusText: 'Подтверждена',
      holdExpiresAtUtc: null,
      cancellation: {
        policy: 'Standard',
        summary: 'Отмена до 00:00 дня заезда — возврат предоплаты полностью.',
        canCancel: true,
        refund: { kind: 'Partial', refundAtLeastRub: 450, maxDeductionRub: 4500, text: 'К возврату не меньше 450 ₽. Компания вправе удержать не больше стоимости первой ночи.' },
        cannotCancelText: null,
      },
    })

  it('asks first: the refund «не меньше X ₽» from the server and who makes the refund (the company, with its phone)', async () => {
    api.get.mockResolvedValue(paid())
    renderPage()
    await screen.findByTestId('booking-status')
    fireEvent.click(screen.getByRole('button', { name: 'Отменить бронь' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByTestId('refund-text')).toHaveTextContent('К возврату не меньше 450 ₽')
    expect(within(dialog).getByText(/Возврат делает компания, а не сервис/)).toBeInTheDocument()
    expect(within(dialog).getByRole('link', { name: '+7 (900) 123-45-67' })).toBeInTheDocument()
    expect(api.cancel).not.toHaveBeenCalled()
  })

  it('confirming cancels and shows the final status; the cancel section goes away', async () => {
    api.get.mockResolvedValue(paid())
    api.cancel.mockResolvedValue(
      bookingFixture({ status: 'CancelledByGuest', displayStatus: 'CancelledByGuest', statusText: 'Отменена гостем', holdExpiresAtUtc: null, payment: null, proofs: { canAttach: false, maxCount: 3, maxBytes: 1, acceptedTypes: [] } }),
    )
    renderPage()
    await screen.findByTestId('booking-status')
    fireEvent.click(screen.getByRole('button', { name: 'Отменить бронь' }))
    const dialog = await screen.findByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Отменить бронь' }))
    await waitFor(() => expect(screen.getByTestId('booking-status')).toHaveTextContent('Отменена гостем'))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Отмена' })).not.toBeInTheDocument()
  })

  it('a refused cancel (409 CancelNotAllowed) shows the server text and the current booking', async () => {
    api.get.mockResolvedValue(paid())
    api.cancel.mockRejectedValue(
      httpError(409, {
        code: 'CancelNotAllowed',
        message: 'Время заезда наступило — по вопросам отмены свяжитесь с компанией: +7 (900) 123-45-67',
        booking: bookingFixture({ status: 'Confirmed', displayStatus: 'Confirmed', statusText: 'Подтверждена', holdExpiresAtUtc: null, cancellation: { ...paid().cancellation, canCancel: false, cannotCancelText: 'Время заезда наступило — по вопросам отмены свяжитесь с компанией: +7 (900) 123-45-67' } }),
      }),
    )
    renderPage()
    await screen.findByTestId('booking-status')
    fireEvent.click(screen.getByRole('button', { name: 'Отменить бронь' }))
    const dialog = await screen.findByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Отменить бронь' }))
    expect(await within(dialog).findByText(/Время заезда наступило/)).toBeInTheDocument()
  })

  it('when the time to cancel has passed the page says so instead of a button', async () => {
    api.get.mockResolvedValue({ ...paid(), cancellation: { ...paid().cancellation, canCancel: false, cannotCancelText: 'Время заезда наступило — по вопросам отмены свяжитесь с компанией: +7 (900) 123-45-67' } })
    renderPage()
    await screen.findByTestId('booking-status')
    expect(screen.queryByRole('button', { name: 'Отменить бронь' })).not.toBeInTheDocument()
    expect(screen.getByText(/Время заезда наступило/)).toBeInTheDocument()
  })
})

describe('BookingPage — confirmed and final', () => {
  it('shows the check-in information once the server released it', async () => {
    api.get.mockResolvedValue(
      bookingFixture({ status: 'Confirmed', displayStatus: 'Confirmed', statusText: 'Подтверждена', holdExpiresAtUtc: null, checkInInfo: { companyText: 'Код замка 1234', houseText: 'Ключ в сейфе' } }),
    )
    renderPage()
    const info = await screen.findByTestId('checkin-info')
    expect(info).toHaveTextContent('Код замка 1234')
    expect(info).toHaveTextContent('Ключ в сейфе')
  })

  it('does not draw an empty check-in block before the release', async () => {
    api.get.mockResolvedValue(bookingFixture({ status: 'Confirmed', displayStatus: 'Confirmed', statusText: 'Подтверждена', holdExpiresAtUtc: null, checkInInfo: null }))
    renderPage()
    await screen.findByTestId('booking-status')
    expect(screen.queryByTestId('checkin-info')).not.toBeInTheDocument()
  })

  it('a rejected payment shows the reason, the outcome text and the company phone', async () => {
    api.get.mockResolvedValue(
      bookingFixture({
        status: 'PaymentRejected',
        displayStatus: 'PaymentRejected',
        statusText: 'Оплата не подтверждена',
        holdExpiresAtUtc: null,
        payment: null,
        statusReason: 'Платёж не поступил',
        outcomeText: 'Оплата не подтверждена. Если вы платили, компания обязана вернуть деньги или восстановить бронь.',
      }),
    )
    renderPage()
    const outcome = await screen.findByTestId('booking-outcome')
    expect(outcome).toHaveTextContent('Платёж не поступил')
    expect(outcome).toHaveTextContent('обязана вернуть деньги или восстановить бронь')
    expect(within(outcome).getByRole('link', { name: '+7 (900) 123-45-67' })).toBeInTheDocument()
  })

  it('shows the full provider (the name of a private person appears here, ЮР-3) and the tourist tax note', async () => {
    api.get.mockResolvedValue(
      bookingFixture({ provider: { status: 'SelfEmployed', statusLabel: 'Плательщик налога на профессиональный доход', name: 'Иванов Иван Иванович', inn: '123456789012', ogrn: null, claimsAddress: 'Новокузнецк, ул. Мира, 1' } }),
    )
    renderPage()
    await screen.findByTestId('booking-status')
    expect(screen.getByText('Иванов Иван Иванович')).toBeInTheDocument()
    expect(screen.getByText('Новокузнецк, ул. Мира, 1')).toBeInTheDocument()
    expect(screen.getByText(/Цена проживания указана без туристического налога/)).toBeInTheDocument()
  })
})

describe('BookingPage — final statuses promise no refund', () => {
  for (const status of ['ExpiredUnpaid', 'PaymentRejected', 'CancelledByGuest', 'CancelledByOwner'] as const) {
    it(`${status}: no «К возврату не меньше» and no cancel button`, async () => {
      api.get.mockResolvedValue(
        bookingFixture({
          status,
          displayStatus: status,
          holdExpiresAtUtc: null,
          payment: null,
          cancellation: {
            policy: 'Standard',
            summary: '',
            canCancel: false,
            refund: { kind: 'NothingPaid', refundAtLeastRub: 0, maxDeductionRub: 0, text: 'Бронь уже не действует — отменять нечего' },
            cannotCancelText: null,
          },
        }),
      )
      renderPage()
      await screen.findByTestId('booking-status')
      expect(document.body.textContent ?? '').not.toMatch(/К возврату не меньше/)
      expect(screen.queryByRole('button', { name: 'Отменить бронь' })).not.toBeInTheDocument()
    })
  }
})
