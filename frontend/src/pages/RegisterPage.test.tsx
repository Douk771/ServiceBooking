import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RegisterPage } from './RegisterPage'
import { useAuthStore } from '../store/authStore'
import type { LegalManifest } from '../types'

const register = vi.fn()
const getManifest = vi.fn()
const grant = vi.fn()
const navigate = vi.fn()

vi.mock('../api/auth', () => ({
  authApi: { register: (...args: unknown[]) => register(...args) },
}))
vi.mock('../api/legal', () => ({
  legalApi: { getManifest: (...args: unknown[]) => getManifest(...args) },
}))
vi.mock('../api/consents', () => ({
  consentsApi: { grant: (...args: unknown[]) => grant(...args) },
}))
vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router-dom')>()
  return { ...actual, useNavigate: () => navigate }
})

function manifest(overrides: Partial<LegalManifest> = {}): LegalManifest {
  return {
    documents: [
      { type: 'Privacy', title: 'Политика', version: '2026-09-21', effectiveFrom: '2026-09-21', isDraft: true, changeKind: 'Material', gate: 'Global', url: '/privacy' },
      { type: 'TermsClient', title: 'Соглашение', version: '2026-09-21', effectiveFrom: '2026-09-21', isDraft: true, changeKind: 'Material', gate: 'Global', url: '/terms' },
      {
        type: 'PdnConsent',
        title: 'Согласие на обработку ПДн',
        version: '2026-09-15',
        effectiveFrom: '2026-09-15',
        isDraft: true,
        changeKind: 'Material',
        gate: 'None',
        url: '/pdn-consent',
        purposes: [
          { key: 'ProviderDelivery', title: 'Передача привлекаемым лицам для доставки уведомлений' },
          { key: 'WorkPhotos', title: 'Фотофиксация выполненной работы' },
        ],
      },
    ],
    uiTexts: [],
    ...overrides,
  }
}

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <RegisterPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

async function fillAccountFields(user: ReturnType<typeof userEvent.setup>) {
  await user.type(await screen.findByLabelText('Имя'), 'Иван')
  await user.type(screen.getByLabelText('Фамилия'), 'Петров')
  await user.type(screen.getByLabelText('Телефон'), '+79991234567')
  await user.type(screen.getByLabelText('Пароль'), 'Password123')
}

beforeEach(() => {
  register.mockReset()
  getManifest.mockReset()
  grant.mockReset()
  navigate.mockReset()
  useAuthStore.setState({ user: null, token: null })
})

describe('RegisterPage', () => {
  it('shows a retry state when the legal manifest fails to load, and never shows the form', async () => {
    getManifest.mockRejectedValueOnce(new Error('network'))
    renderPage()

    expect(await screen.findByRole('button', { name: 'Попробовать снова' })).toBeInTheDocument()
    expect(screen.queryByLabelText('Имя')).not.toBeInTheDocument()
  })

  it('keeps submit disabled until BOTH the privacy acknowledgement and the terms acceptance are checked', async () => {
    const user = userEvent.setup()
    getManifest.mockResolvedValue(manifest())
    renderPage()

    const submit = await screen.findByRole('button', { name: 'Зарегистрироваться' })
    expect(submit).toBeDisabled()

    await user.click(screen.getByLabelText(/Политикой обработки персональных данных/))
    expect(submit).toBeDisabled()

    await user.click(screen.getByLabelText(/Пользовательское соглашение/))
    expect(submit).not.toBeDisabled()
  })

  it('registers with a `legal` object built from the fetched document versions — not `acceptedLegal`', async () => {
    const user = userEvent.setup()
    getManifest.mockResolvedValue(manifest())
    register.mockResolvedValueOnce({
      token: 'tok',
      userId: 'u1',
      phone: '+79991234567',
      firstName: 'Иван',
      lastName: 'Петров',
      roles: ['Client'],
    })
    renderPage()

    await fillAccountFields(user)
    await user.click(await screen.findByLabelText(/Политикой обработки персональных данных/))
    await user.click(screen.getByLabelText(/Пользовательское соглашение/))
    await user.click(screen.getByRole('button', { name: 'Зарегистрироваться' }))

    await waitFor(() =>
      expect(register).toHaveBeenCalledWith(
        expect.objectContaining({
          legal: { privacyAcknowledgedVersion: '2026-09-21', termsAcceptedVersion: '2026-09-21' },
        }),
      ),
    )
    expect(register.mock.calls[0][0]).not.toHaveProperty('acceptedLegal')
    await waitFor(() => expect(useAuthStore.getState().token).toBe('tok'))
    await waitFor(() => expect(navigate).toHaveBeenCalledWith('/'))
  })

  it('makes the second, separate consents call only when a purpose was checked — with no purpose checked, it is not called', async () => {
    const user = userEvent.setup()
    getManifest.mockResolvedValue(manifest())
    register.mockResolvedValueOnce({
      token: 'tok',
      userId: 'u1',
      phone: '+79991234567',
      firstName: 'Иван',
      lastName: 'Петров',
      roles: ['Client'],
    })
    renderPage()

    await fillAccountFields(user)
    await user.click(await screen.findByLabelText(/Политикой обработки персональных данных/))
    await user.click(screen.getByLabelText(/Пользовательское соглашение/))
    await user.click(screen.getByRole('button', { name: 'Зарегистрироваться' }))

    await waitFor(() => expect(register).toHaveBeenCalled())
    await waitFor(() => expect(navigate).toHaveBeenCalledWith('/'))
    expect(grant).not.toHaveBeenCalled()
  })

  it('checking a purpose sends it in the second call, after registration succeeds', async () => {
    const user = userEvent.setup()
    getManifest.mockResolvedValue(manifest())
    register.mockResolvedValueOnce({
      token: 'tok',
      userId: 'u1',
      phone: '+79991234567',
      firstName: 'Иван',
      lastName: 'Петров',
      roles: ['Client'],
    })
    grant.mockResolvedValueOnce({})
    renderPage()

    await fillAccountFields(user)
    await user.click(await screen.findByLabelText(/Политикой обработки персональных данных/))
    await user.click(screen.getByLabelText(/Пользовательское соглашение/))
    await user.click(screen.getByLabelText('Фотофиксация выполненной работы'))
    await user.click(screen.getByRole('button', { name: 'Зарегистрироваться' }))

    await waitFor(() => expect(grant).toHaveBeenCalledWith('PdnConsent', '2026-09-15', ['WorkPhotos']))
    await waitFor(() => expect(navigate).toHaveBeenCalledWith('/'))
  })

  it('still navigates away even if the second, non-blocking consents call fails', async () => {
    const user = userEvent.setup()
    getManifest.mockResolvedValue(manifest())
    register.mockResolvedValueOnce({
      token: 'tok',
      userId: 'u1',
      phone: '+79991234567',
      firstName: 'Иван',
      lastName: 'Петров',
      roles: ['Client'],
    })
    grant.mockRejectedValueOnce(new Error('network'))
    renderPage()

    await fillAccountFields(user)
    await user.click(await screen.findByLabelText(/Политикой обработки персональных данных/))
    await user.click(screen.getByLabelText(/Пользовательское соглашение/))
    await user.click(screen.getByLabelText('Фотофиксация выполненной работы'))
    await user.click(screen.getByRole('button', { name: 'Зарегистрироваться' }))

    await waitFor(() => expect(grant).toHaveBeenCalled())
    await waitFor(() => expect(navigate).toHaveBeenCalledWith('/'))
  })
})
