import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { NumbersBlock } from './NumbersBlock'
import { maxTransport, maxTrialOnTermsStep, numbersOverview, closedWhatsAppWithNumber } from '../../test/fixtures/numbersOverview'

const overview = vi.fn()
const request = vi.fn()
const connect = vi.fn()
const getQr = vi.fn()

vi.mock('../../api/notificationNumbers', async (orig) => ({
  ...(await orig<typeof import('../../api/notificationNumbers')>()),
  notificationNumbersApi: { overview: () => overview(), request: (p: unknown) => request(p) },
}))
vi.mock('../../api/notificationChannels', () => ({
  notificationChannelsApi: { connect: (id: string) => connect(id), getQr: (id: string) => getQr(id), replace: vi.fn(), disconnect: vi.fn() },
}))

function renderBlock() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <NumbersBlock />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => vi.clearAllMocks())

describe('NumbersBlock', () => {
  it('показывает загрузку, затем сервер-тексты дословно', async () => {
    overview.mockResolvedValue(numbersOverview([closedWhatsAppWithNumber()]))
    renderBlock()
    expect(screen.getByRole('status', { name: 'Загрузка номеров' })).toBeInTheDocument()
    expect(await screen.findByText('Сообщения уходят с номера +7 *** ***-45-67')).toBeInTheDocument()
    expect(screen.getByText('Работает для всех ваших компаний: 1')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Отправить заявку' })).not.toBeInTheDocument()
  })

  it('пустое состояние при 0 транспортов', async () => {
    overview.mockResolvedValue(numbersOverview([]))
    renderBlock()
    expect(await screen.findByText('Подключение мессенджеров сейчас недоступно')).toBeInTheDocument()
  })

  it('ошибка API: сообщение и повтор', async () => {
    overview.mockRejectedValueOnce(new Error('x')).mockResolvedValue(numbersOverview([maxTransport()]))
    renderBlock()
    await userEvent.click(await screen.findByRole('button', { name: 'Повторить' }))
    // after the retry the block is back; a transport without a number shows its price and the entry button of the wizard step (no status text of its own)
    expect(await screen.findByRole('button', { name: 'Отправить заявку' })).toBeInTheDocument()
  })

  it('триал: мастер открывается на «Условия», кнопка активна лишь с обеими отметками, paymentRequest=false', async () => {
    overview.mockResolvedValue(numbersOverview([maxTrialOnTermsStep()]))
    request.mockResolvedValue({ id: 'ch1' })
    connect.mockResolvedValue({ state: 'Connecting', refreshAfterSeconds: 3 })
    getQr.mockResolvedValue({ state: 'Connecting', qrBase64: null, refreshAfterSeconds: 60, expiresInSeconds: 0 })
    renderBlock()
    await userEvent.click(await screen.findByRole('button', { name: 'Принять условия' }))
    expect(screen.getByRole('dialog', { name: 'Условия подключения MAX' })).toBeInTheDocument()
    const submit = screen.getByRole('button', { name: 'Принять и продолжить' })
    await userEvent.type(screen.getByLabelText('ИНН'), '7707083893')
    await userEvent.click(screen.getByLabelText(/Я ознакомлен/))
    expect(submit).toBeDisabled()
    await userEvent.click(screen.getByLabelText('Я прочитал(а) и принимаю риски'))
    expect(submit).toBeEnabled()
    await userEvent.click(submit)
    await waitFor(() => expect(request).toHaveBeenCalled())
    expect(request.mock.calls[0][0]).toMatchObject({
      paymentRequest: false,
      transport: 'Max',
      offerAccepted: { version: '2026-10-20' },
      riskAccepted: { version: '2026-10-20' },
    })
    await waitFor(() => expect(connect).toHaveBeenCalledWith('ch1'))
    expect(screen.getByText('Отключите пароль входа в настройках MAX')).toBeInTheDocument()
  })

  it('оплата: paymentRequest=true и текст «Заявка отправлена»', async () => {
    overview.mockResolvedValue(numbersOverview([maxTransport()]))
    request.mockResolvedValue({ id: 'ch2' })
    renderBlock()
    await userEvent.click(await screen.findByRole('button', { name: 'Отправить заявку' }))
    await userEvent.type(screen.getByLabelText('ИНН'), '7707083893')
    await userEvent.click(screen.getByLabelText(/Я ознакомлен/))
    await userEvent.click(screen.getByLabelText('Я прочитал(а) и принимаю риски'))
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Отправить заявку' }))
    expect(await screen.findByText(/Заявка отправлена\. Администратор свяжется/)).toBeInTheDocument()
    expect(request.mock.calls[0][0].paymentRequest).toBe(true)
  })

  it('ошибка сервера в мастере показывается дословно', async () => {
    overview.mockResolvedValue(numbersOverview([maxTransport()]))
    request.mockRejectedValue({ response: { status: 409, data: 'Подключение MAX сейчас недоступно' } })
    renderBlock()
    await userEvent.click(await screen.findByRole('button', { name: 'Отправить заявку' }))
    await userEvent.type(screen.getByLabelText('ИНН'), '7707083893')
    await userEvent.click(screen.getByLabelText(/Я ознакомлен/))
    await userEvent.click(screen.getByLabelText('Я прочитал(а) и принимаю риски'))
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Отправить заявку' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Подключение MAX сейчас недоступно')
  })
})
