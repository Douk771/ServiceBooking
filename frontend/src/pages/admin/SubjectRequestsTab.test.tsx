import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { SubjectRequestsTab } from './SubjectRequestsTab'
import type { SubjectRequestDto } from '../../types'

const adminList = vi.fn()
const adminSetStatus = vi.fn()
const adminRegister = vi.fn()

vi.mock('../../api/subjectRequests', () => ({
  subjectRequestsApi: {
    adminList: (...args: unknown[]) => adminList(...args),
    adminSetStatus: (...args: unknown[]) => adminSetStatus(...args),
    adminRegister: (...args: unknown[]) => adminRegister(...args),
  },
}))

function request(overrides: Partial<SubjectRequestDto> = {}): SubjectRequestDto {
  return {
    id: 'r1',
    reference: 'SR-0001',
    kind: 'Access',
    status: 'Received',
    phoneMasked: '+7 *** *** 12 34',
    contactValue: 'user@example.com',
    message: 'Хочу получить свои данные',
    receivedAt: '2026-09-20T10:00:00Z',
    dueAt: '2026-10-01T10:00:00Z',
    dueState: 'OnTime',
    answeredAt: null,
    handlerName: null,
    resolution: null,
    ...overrides,
  }
}

function renderTab() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <SubjectRequestsTab />
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  adminList.mockResolvedValue({ items: [request()], page: 1, pageSize: 20, total: 1, hasNext: false })
})

// API_CONTRACT_CYCLE20.md §438 (US-20-09) — channel column.
describe('SubjectRequestsTab — channel column (US-20-09)', () => {
  it('shows "Веб-форма" when channel is absent (stale cache from before the cycle)', async () => {
    adminList.mockResolvedValue({ items: [request()], page: 1, pageSize: 20, total: 1, hasNext: false })
    renderTab()
    expect(await screen.findByText('Веб-форма')).toBeInTheDocument()
  })

  it('shows the actual channel and who registered it for a manually-registered request', async () => {
    adminList.mockResolvedValue({
      items: [request({ channel: 'PostalMail', registeredByName: 'Оператор Иванов' })],
      page: 1,
      pageSize: 20,
      total: 1,
      hasNext: false,
    })
    renderTab()
    expect(await screen.findByText('Почта')).toBeInTheDocument()
    expect(screen.getByText(/зарегистрировал\(а\) Оператор Иванов/)).toBeInTheDocument()
  })
})

describe('SubjectRequestsTab — manual registration (US-20-09)', () => {
  it('opens the modal, blocks submit until contact and message are filled, then registers with the picked channel', async () => {
    const user = userEvent.setup()
    adminRegister.mockResolvedValueOnce(request({ id: 'r2', channel: 'Email' }))
    renderTab()

    await user.click(await screen.findByRole('button', { name: 'Зарегистрировать обращение' }))
    expect(await screen.findByText('Зарегистрировать обращение, пришедшее не через форму')).toBeInTheDocument()

    const submit = screen.getByRole('button', { name: 'Зарегистрировать' })
    expect(submit).toBeDisabled()

    await user.type(screen.getByLabelText('Контакт для ответа'), 'user@example.com')
    await user.type(screen.getByLabelText('Текст обращения'), 'Прошу отозвать согласие')
    expect(submit).toBeEnabled()

    await user.click(submit)

    await waitFor(() => expect(adminRegister).toHaveBeenCalledTimes(1))
    const payload = adminRegister.mock.calls[0][0]
    expect(payload.channel).toBe('Email')
    expect(payload.contactValue).toBe('user@example.com')
    expect(payload.message).toBe('Прошу отозвать согласие')
    // WebForm must never be reachable from this form — only Email/PostalMail are offered.
    expect(payload.channel).not.toBe('WebForm')
  })

  it('lets PostalMail be picked instead of the Email default', async () => {
    const user = userEvent.setup()
    adminRegister.mockResolvedValueOnce(request({ id: 'r3', channel: 'PostalMail' }))
    renderTab()

    await user.click(await screen.findByRole('button', { name: 'Зарегистрировать обращение' }))
    await user.selectOptions(screen.getByLabelText('Канал'), 'PostalMail')
    await user.type(screen.getByLabelText('Контакт для ответа'), 'г. Барнаул, а/я 12')
    await user.type(screen.getByLabelText('Текст обращения'), 'Жалоба')

    await user.click(screen.getByRole('button', { name: 'Зарегистрировать' }))

    await waitFor(() => expect(adminRegister).toHaveBeenCalledTimes(1))
    expect(adminRegister.mock.calls[0][0].channel).toBe('PostalMail')
  })
})
