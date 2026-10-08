import { describe, it, expect, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Routes, Route, useLocation } from 'react-router-dom'
import { ProtectedRoute } from '../components/auth/ProtectedRoute'
import { useAuthStore } from '../store/authStore'
import { withOwnerRole } from '../utils/ownerRole'
import { zapisLanding } from './home/zapisLanding'

function Where() {
  const l = useLocation()
  return <div>at:{l.pathname}{l.search}</div>
}
const tree = (initial: string) =>
  render(
    <MemoryRouter initialEntries={[initial]}>
      <Routes>
        <Route path="/cabinet/new" element={<ProtectedRoute returnToPath="/cabinet/new"><div>create-form</div></ProtectedRoute>} />
        <Route path="/cabinet" element={<ProtectedRoute roles={['Master', 'CompanyOwner']} returnToPath="/cabinet" deniedTo="/cabinet/new"><div>cabinet</div></ProtectedRoute>} />
        <Route path="*" element={<Where />} />
      </Routes>
    </MemoryRouter>,
  )
const user = (roles: string[]) => ({ id: '1', phone: '+7', firstName: 'a', lastName: 'b', roles })

describe('ProtectedRoute / create company route', () => {
  beforeEach(() => useAuthStore.setState({ user: null, token: null }))

  it('Client without a role can open /cabinet/new', () => {
    useAuthStore.setState({ user: user(['Client']), token: 't' })
    tree('/cabinet/new')
    expect(screen.getByText('create-form')).toBeInTheDocument()
  })
  it('guest is sent to /login with returnTo', () => {
    tree('/cabinet/new')
    expect(screen.getByText('at:/login?returnTo=%2Fcabinet%2Fnew')).toBeInTheDocument()
  })
  it('Client at /cabinet lands on the create route, not a dead end', () => {
    useAuthStore.setState({ user: user(['Client']), token: 't' })
    tree('/cabinet')
    expect(screen.getByText('create-form')).toBeInTheDocument()
  })
  it('withOwnerRole adds CompanyOwner once', () => {
    expect(withOwnerRole(['Client'])).toEqual(['Client', 'CompanyOwner'])
    expect(withOwnerRole(['Client', 'CompanyOwner'])).toEqual(['Client', 'CompanyOwner'])
  })
  it('landing config: connect-salon leads to the create route; guest goes via register with returnTo', () => {
    const a = zapisLanding.business.actions[0]
    expect(a).toMatchObject({ kind: 'auth-route', authedTo: '/cabinet/new' })
    if (a.kind === 'auth-route') {
      expect(a.guestTo).toBe('/register?returnTo=%2Fcabinet%2Fnew')
    }
  })
})
