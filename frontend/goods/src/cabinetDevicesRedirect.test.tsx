import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Navigate, Route, Routes, useLocation } from 'react-router-dom'

function Profile() {
  const { pathname, hash } = useLocation()
  return <p>{pathname + hash}</p>
}

// The same <Route> as in GoodsApp.tsx (§33.8): the old devices page forwards to the profile block.
describe('/cabinet/devices redirect', () => {
  it('goes to /profile#devices', () => {
    render(
      <MemoryRouter initialEntries={['/cabinet/devices']}>
        <Routes>
          <Route path="/cabinet/devices" element={<Navigate to="/profile#devices" replace />} />
          <Route path="/profile" element={<Profile />} />
        </Routes>
      </MemoryRouter>,
    )
    expect(screen.getByText('/profile#devices')).toBeInTheDocument()
  })
})
