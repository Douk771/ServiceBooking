import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClientProvider, QueryClient } from '@tanstack/react-query'
import { LoginPage } from './LoginPage'

// Regression for the review #2 finding: LoginPage.tsx now uses the Russian-only PhoneInput mask by
// default, and toCanonicalPhone blanks a foreign number entirely — making an account with a foreign
// number physically impossible to log into, even though the server (AuthController.cs) deliberately
// still accepts it (ARCHITECTURE_CYCLE6.md §48.4).
//
// `SPEC_CYCLE6_BOOKING_FIXES.md` §0.1 Q8 follow-up: the fix is a soft mask (PhoneInput `restrictToRussia={false}`) that
// holds the `+7 (900) 000-00-00` mask while input looks Russian and releases it, unmangled, once a
// foreign country code is typed — no client-side normalization happens for login, the raw text goes
// straight to the server (`PhoneNormalizer.Normalize` extracts digits from anything).

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

describe('LoginPage — foreign phone number sign-in (ARCHITECTURE_CYCLE6.md §48.4)', () => {
  it('does not blank out a foreign (+380…) number and submits it, unnormalized, to the server', async () => {
    const user = userEvent.setup()
    renderPage()

    await user.type(screen.getByPlaceholderText('+7 (900) 000-00-00'), '+380671234567')
    await user.type(screen.getByPlaceholderText('••••••••'), 'secret123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    await waitFor(() => expect(login).toHaveBeenCalled())
    // No client-side normalization for login (§48.3's "one normalizer" rule) — the raw text the user
    // typed goes straight to the server, which extracts digits itself (PhoneNormalizer.Normalize).
    expect(login).toHaveBeenCalledWith('+380671234567', 'secret123')
  })

  it('keeps the Russian +7 mask for a Russian-shaped number', async () => {
    const user = userEvent.setup()
    renderPage()

    await user.type(screen.getByPlaceholderText('+7 (900) 000-00-00'), '89990001122')
    expect(screen.getByPlaceholderText('+7 (900) 000-00-00')).toHaveValue('+7 (999) 000-11-22')

    await user.type(screen.getByPlaceholderText('••••••••'), 'secret123')
    await user.click(screen.getByRole('button', { name: 'Войти' }))

    await waitFor(() => expect(login).toHaveBeenCalled())
    expect(login).toHaveBeenCalledWith('79990001122', 'secret123')
  })
})
