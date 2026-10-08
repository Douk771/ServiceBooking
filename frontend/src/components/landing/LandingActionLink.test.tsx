import { describe, it, expect, afterEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { LandingActionLink } from './LandingActionLink'
import { useAuthStore } from '../../store/authStore'

const r = (ui: React.ReactElement) => render(<MemoryRouter>{ui}</MemoryRouter>)
afterEach(() => useAuthStore.setState({ token: null } as never))

describe('LandingActionLink (T38-04)', () => {
  it('anchor renders a hash link', () => {
    r(<LandingActionLink primary action={{ kind: 'anchor', href: '#x', label: 'К якорю' }} />)
    expect(screen.getByRole('link', { name: 'К якорю' })).toHaveAttribute('href', '#x')
  })
  it('route renders a router link; arrow only on primary', () => {
    const { container } = r(<LandingActionLink primary={false} action={{ kind: 'route', to: '/orders', label: 'Заказы' }} />)
    expect(screen.getByRole('link', { name: 'Заказы' })).toHaveAttribute('href', '/orders')
    expect(container.querySelector('svg')).toBeNull()
  })
  it('auth-route switches target by auth state', () => {
    const action = { kind: 'auth-route', guestTo: '/register', authedTo: '/cabinet', label: 'Go' } as const
    const { unmount } = r(<LandingActionLink primary action={action} />)
    expect(screen.getByRole('link', { name: 'Go' })).toHaveAttribute('href', '/register')
    unmount()
    const spy = useAuthStore.getState()
    useAuthStore.setState({ isAuthenticated: () => true } as never)
    r(<LandingActionLink primary action={action} />)
    expect(screen.getByRole('link', { name: 'Go' })).toHaveAttribute('href', '/cabinet')
    useAuthStore.setState({ isAuthenticated: spy.isAuthenticated } as never)
  })
})
