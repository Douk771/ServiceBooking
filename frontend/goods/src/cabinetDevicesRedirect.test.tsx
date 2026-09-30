import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { CABINET_DEVICES_PATH, cabinetDevicesRedirect } from './cabinetDevicesRoute'

function Profile() {
  const { pathname, hash } = useLocation()
  return <p>{pathname + hash}</p>
}

// Path and element are imported from the same module GoodsApp uses (§33.8).
describe('/cabinet/devices redirect', () => {
  it('goes to /profile#devices', () => {
    render(
      <MemoryRouter initialEntries={['/cabinet/devices']}>
        <Routes>
          <Route path={CABINET_DEVICES_PATH} element={cabinetDevicesRedirect} />
          <Route path="/profile" element={<Profile />} />
        </Routes>
      </MemoryRouter>,
    )
    expect(screen.getByText('/profile#devices')).toBeInTheDocument()
  })
})
