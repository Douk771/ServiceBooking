import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClientProvider, QueryClient } from '@tanstack/react-query'
import { LoginPage } from './LoginPage'

// Regression for the review #2 finding: LoginPage.tsx now uses the Russian-only PhoneInput mask by
// default, and toCanonicalPhone blanks a foreign number entirely — making an account with a foreign
// number physically impossible to log into, even though the server (AuthController.cs) deliberately
// still accepts it (ARCHITECTURE_CYCLE6.md §947).

const login = vi.fn()

vi.mock('../api/auth', () => ({
  authApi: {
    login: (...args: unknown[]) => login(...args),
  },
}))

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <LoginPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  login.mockReset().mockResolvedValue({
    userId: 'u1',
    phone: '380671234567',
    email: null,
    firstName: 'Ivan',
    lastName: 'Ivanov',
    roles: ['Client'],
    token: 'tok',
  })
})

describe('LoginPage — foreign phone number sign-in (ARCHITECTURE_CYCLE6.md §947)', () => {
  it('does not blank out a foreign (+380…) number and submits its digits to the server', async () => {
    const user = userEvent.setup()
    renderPage()

    await user.type(screen.getByPlaceholderText('+7 (900) 000-00-00'), '+380671234567')
    await user.type(screen.getByPlaceholderText('••••••••'), 'secret123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    await waitFor(() => expect(login).toHaveBeenCalled())
    expect(login).toHaveBeenCalledWith('380671234567', 'secret123')
  })
})
