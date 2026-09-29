import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { EmbedPage } from './EmbedPage'

const getBySlug = vi.fn()
const getByCompany = vi.fn()
vi.mock('../api/companies', () => ({ companiesApi: { getBySlug: (...a: unknown[]) => getBySlug(...a) } }))
vi.mock('../api/services', () => ({ servicesApi: { getByCompany: (...a: unknown[]) => getByCompany(...a) } }))

beforeEach(() => {
  getBySlug.mockReset()
  getByCompany.mockReset().mockResolvedValue([])
})

function renderEmbed() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/embed/shop']}>
        <Routes>
          <Route path="/embed/:slug" element={<EmbedPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('EmbedPage — cycle 23 (§389.3-2)', () => {
  it('shows «нет онлайн-записи» for a shop and never asks for services', async () => {
    getBySlug.mockResolvedValue({ id: 'c1', name: 'Шаурма', slug: 'shop', allowSelfBooking: true, kind: 'Orders' })
    renderEmbed()
    expect(await screen.findByText('У этой компании нет онлайн-записи')).toBeInTheDocument()
    expect(getByCompany).not.toHaveBeenCalled()
  })
})
