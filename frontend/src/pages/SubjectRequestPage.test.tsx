import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { SubjectRequestPage } from './SubjectRequestPage'

const submit = vi.fn()

vi.mock('../api/subjectRequests', () => ({
  subjectRequestsApi: { submit: (...args: unknown[]) => submit(...args) },
}))

// The real widget needs a live script + VITE_SMARTCAPTCHA_SITEKEY; stubbed out here the same way the
// booking flow itself would need to, so this test exercises the page's own logic, not Yandex's widget.
vi.mock('../components/booking/SmartCaptcha', () => ({
  smartCaptchaEnabled: false,
  SmartCaptcha: () => null,
}))

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <SubjectRequestPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  submit.mockReset()
})

describe('SubjectRequestPage', () => {
  it('keeps submit disabled until phone, contact and message are all filled', async () => {
    const user = userEvent.setup()
    renderPage()

    const button = screen.getByRole('button', { name: 'Отправить обращение' })
    expect(button).toBeDisabled()

    await user.type(screen.getByLabelText(/Номер телефона/), '+79991234567')
    expect(button).toBeDisabled()
    await user.type(screen.getByLabelText(/Контакт для ответа/), 'me@example.com')
    expect(button).toBeDisabled()
    await user.type(screen.getByLabelText('Опишите обращение'), 'Прошу удалить мои данные')
    expect(button).not.toBeDisabled()
  })

  it('submits the selected kind along with the other fields', async () => {
    const user = userEvent.setup()
    submit.mockResolvedValueOnce({ reference: 'SR-7K3Q2M', responseDueByWorkingDays: 10 })
    renderPage()

    await user.selectOptions(screen.getByLabelText('Тип обращения'), 'Erasure')
    await user.type(screen.getByLabelText(/Номер телефона/), '+79991234567')
    await user.type(screen.getByLabelText(/Контакт для ответа/), 'me@example.com')
    await user.type(screen.getByLabelText('Опишите обращение'), 'Прошу удалить мои данные')
    await user.click(screen.getByRole('button', { name: 'Отправить обращение' }))

    await waitFor(() =>
      expect(submit).toHaveBeenCalledWith({
        kind: 'Erasure',
        phone: '+79991234567',
        contactValue: 'me@example.com',
        message: 'Прошу удалить мои данные',
        captchaToken: undefined,
      }),
    )
  })

  it('shows the reference number and the response deadline on success — nothing about the subject', async () => {
    const user = userEvent.setup()
    submit.mockResolvedValueOnce({ reference: 'SR-7K3Q2M', responseDueByWorkingDays: 10 })
    renderPage()

    await user.type(screen.getByLabelText(/Номер телефона/), '+79991234567')
    await user.type(screen.getByLabelText(/Контакт для ответа/), 'me@example.com')
    await user.type(screen.getByLabelText('Опишите обращение'), 'Прошу удалить мои данные')
    await user.click(screen.getByRole('button', { name: 'Отправить обращение' }))

    expect(await screen.findByText('SR-7K3Q2M')).toBeInTheDocument()
    expect(screen.getByText(/10 рабочих дней/)).toBeInTheDocument()
  })

  it('shows a rate-limit message on 429', async () => {
    const user = userEvent.setup()
    submit.mockRejectedValueOnce({
      isAxiosError: true,
      response: { status: 429, data: 'Слишком много обращений. Попробуйте позже.' },
    })
    renderPage()

    await user.type(screen.getByLabelText(/Номер телефона/), '+79991234567')
    await user.type(screen.getByLabelText(/Контакт для ответа/), 'me@example.com')
    await user.type(screen.getByLabelText('Опишите обращение'), 'Прошу удалить мои данные')
    await user.click(screen.getByRole('button', { name: 'Отправить обращение' }))

    expect(await screen.findByText('Слишком много обращений. Попробуйте позже.')).toBeInTheDocument()
  })
})
