import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AccountMessengersBlock } from './AccountMessengersBlock'
import type { AdminMessengerLine } from '../../types'

const messengerQuote = vi.fn()
const extendMessenger = vi.fn()
const disableMessenger = vi.fn()
vi.mock('../../api/adminBilling', () => ({
  adminBillingApi: {
    messengerQuote: (...a: unknown[]) => messengerQuote(...a),
    extendMessenger: (...a: unknown[]) => extendMessenger(...a),
    disableMessenger: (...a: unknown[]) => disableMessenger(...a),
  },
}))

const line = (o: Partial<AdminMessengerLine> = {}): AdminMessengerLine => ({
  transport: 'Max', label: 'MAX', optionId: 'o-max', status: 'NotConnected', statusText: 'Не подключён', pricePerMonth: 490, priceText: '490 ₽/мес',
  monthlyPrice: 0, countsInTotal: false, paidUntil: null, isTrial: false, endsAt: null,
  number: { state: 'None', text: 'Номера нет', phoneMasked: null, channelId: null },
  sale: { sellable: true, reason: null, text: 'Продаётся' }, canExtend: true, extendActionText: 'Подключить', extendDisabledText: null,
  canDisable: false, disableNowConfirmText: null, disablePeriodEndConfirmText: null, ...o,
})
const wa = (o: Partial<AdminMessengerLine> = {}) => line({
  transport: 'WhatsApp', label: 'WhatsApp', optionId: 'o-wa', pricePerMonth: null, priceText: 'Цена не задана', canExtend: false,
  sale: { sellable: false, reason: 'NoPrice', text: 'Не продаётся: цена не задана' }, extendDisabledText: 'Не продаётся: цена не задана', ...o,
})
const quote = (months: number) => ({
  transport: 'Max', months, pricePerMonth: 490, amount: months * 490, amountText: `${months} мес. × 490 ₽ = ${months * 490} ₽`, currentPaidUntil: null,
  newPaidUntil: '2027-01-20T00:00:00Z', newPaidUntilText: 'Будет оплачено до 20.01.2027', actionText: `Подключить на ${months} мес.`,
})

function renderBlock(lines: AdminMessengerLine[], total = 0) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <AccountMessengersBlock account={{ id: 'acc1', messengers: lines, messengersMonthlyTotal: total }} />
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  messengerQuote.mockReset(); extendMessenger.mockReset(); disableMessenger.mockReset()
  messengerQuote.mockImplementation((_a: string, _t: string, m: number) => Promise.resolve(quote(m)))
})

describe('AccountMessengersBlock (cycle 43, US-43-03)', () => {
  it('renders nothing for an older server without messengers[]', () => {
    const { container } = render(<QueryClientProvider client={new QueryClient()}><AccountMessengersBlock account={{ id: 'a' }} /></QueryClientProvider>)
    expect(container).toBeEmptyDOMElement()
  })

  it('shows both lines with the server status, price and number text, the total, and the reason a messenger cannot be connected', () => {
    renderBlock([wa(), line({ status: 'Paid', statusText: 'Оплачено до 20.11.2026', monthlyPrice: 490, countsInTotal: true, canExtend: true, extendActionText: 'Продлить' })], 490)
    const w = screen.getByTestId('messenger-line-WhatsApp')
    expect(within(w).getByText('Не подключён')).toBeInTheDocument()
    expect(within(w).getAllByText('Не продаётся: цена не задана').length).toBeGreaterThan(0)
    expect(within(w).getByRole('button', { name: 'Подключить WhatsApp' })).toBeDisabled()
    const m = screen.getByTestId('messenger-line-Max')
    expect(within(m).getByText('Оплачено до 20.11.2026')).toBeInTheDocument()
    expect(within(m).getByRole('button', { name: 'Продлить MAX' })).toBeEnabled()
    expect(within(screen.getByTestId('account-messengers')).getByText('Мессенджеры в месяц').nextSibling).toHaveTextContent(/490/)
  })

  it('asks for the quote when the number of months changes and sends the quoted price as expectedPricePerMonth', async () => {
    const user = userEvent.setup()
    extendMessenger.mockResolvedValue({ id: 'acc1' })
    renderBlock([line()])
    await user.click(screen.getByRole('button', { name: 'Подключить MAX' }))
    const dialog = screen.getByRole('dialog')
    expect(await within(dialog).findByText('1 мес. × 490 ₽ = 490 ₽')).toBeInTheDocument()
    const months = within(dialog).getByLabelText('На сколько месяцев (1–24)')
    await user.clear(months)
    await user.type(months, '3')
    expect(await within(dialog).findByText('3 мес. × 490 ₽ = 1470 ₽')).toBeInTheDocument()
    expect(within(dialog).getByText('Будет оплачено до 20.01.2027')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText('Комментарий (счёт, платёж)'), ' Счёт 5 ')
    await user.click(await within(dialog).findByRole('button', { name: 'Подключить на 3 мес.' }))
    await waitFor(() => expect(extendMessenger).toHaveBeenCalledWith('acc1', 'Max', { months: 3, comment: 'Счёт 5', expectedPricePerMonth: 490 }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('does not ask for a quote and blocks the button while the term is out of range', async () => {
    const user = userEvent.setup()
    renderBlock([line()])
    await user.click(screen.getByRole('button', { name: 'Подключить MAX' }))
    const dialog = screen.getByRole('dialog')
    await within(dialog).findByText('1 мес. × 490 ₽ = 490 ₽')
    messengerQuote.mockClear()
    const months = within(dialog).getByLabelText('На сколько месяцев (1–24)')
    await user.clear(months)
    await user.type(months, '30')
    expect(within(dialog).getByText('Срок — от 1 до 24 месяцев')).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: /Подключить/ })).toBeDisabled()
    expect(messengerQuote).not.toHaveBeenCalledWith('acc1', 'Max', 30)
  })

  it('prints the server refusal of the quote verbatim and keeps the button disabled', async () => {
    const user = userEvent.setup()
    messengerQuote.mockRejectedValue({ response: { status: 409, data: 'У MAX не задана цена. Задайте её во вкладке уведомлений, блок „Подключение мессенджеров“' } })
    renderBlock([line()])
    await user.click(screen.getByRole('button', { name: 'Подключить MAX' }))
    const dialog = screen.getByRole('dialog')
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('У MAX не задана цена.')
    expect(within(dialog).getByRole('button', { name: 'Подключить' })).toBeDisabled()
  })

  it('prints «цена изменилась» of the extend verbatim, stays open and takes a fresh quote', async () => {
    const user = userEvent.setup()
    extendMessenger.mockRejectedValue({ response: { status: 409, data: 'Цена MAX изменилась: теперь 590 ₽/мес. Проверьте сумму и повторите' } })
    renderBlock([line()])
    await user.click(screen.getByRole('button', { name: 'Подключить MAX' }))
    const dialog = screen.getByRole('dialog')
    await user.click(await within(dialog).findByRole('button', { name: 'Подключить на 1 мес.' }))
    expect(await within(dialog).findByText('Цена MAX изменилась: теперь 590 ₽/мес. Проверьте сумму и повторите')).toBeInTheDocument()
    await waitFor(() => expect(messengerQuote.mock.calls.length).toBeGreaterThan(1))
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('disable (P1): hides the button when the server does not allow it; asks for a reason; shows the server confirmation per choice', async () => {
    const user = userEvent.setup()
    disableMessenger.mockResolvedValue({ id: 'acc1' })
    const paid = line({
      status: 'Paid', statusText: 'Оплачено до 20.11.2026', canDisable: true, extendActionText: 'Продлить',
      disableNowConfirmText: 'Деньги не возвращаются. Сообщения клиентам в MAX перестанут уходить с 19.10.2026',
      disablePeriodEndConfirmText: 'Деньги не возвращаются. Сообщения клиентам в MAX перестанут уходить с 20.11.2026',
    })
    renderBlock([wa(), paid])
    expect(screen.queryByRole('button', { name: 'Отключить WhatsApp' })).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Отключить MAX' }))
    const dialog = screen.getByRole('dialog')
    expect(within(dialog).getByText(/с 19\.10\.2026/)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Отключить' }))
    expect(within(dialog).getByText('Укажите причину отключения')).toBeInTheDocument()
    expect(disableMessenger).not.toHaveBeenCalled()
    await user.click(within(dialog).getByLabelText('По окончании оплаченного периода'))
    expect(within(dialog).getByText(/с 20\.11\.2026/)).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText('Причина отключения'), 'по заявлению')
    await user.click(within(dialog).getByRole('button', { name: 'Отключить' }))
    await waitFor(() => expect(disableMessenger).toHaveBeenCalledWith('acc1', 'Max', { when: 'PeriodEnd', comment: 'по заявлению' }))
  })

  it('offers no «по окончании» choice when the server gave no text for it', async () => {
    const user = userEvent.setup()
    renderBlock([line({ status: 'Trial', canDisable: true, disableNowConfirmText: 'Деньги не возвращаются…', disablePeriodEndConfirmText: null })])
    await user.click(screen.getByRole('button', { name: 'Отключить MAX' }))
    expect(screen.queryByLabelText('По окончании оплаченного периода')).not.toBeInTheDocument()
  })
})
