import type { ReactElement } from 'react'
import { render } from '@testing-library/react'
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { StaysCompanyManageDto } from '../types'

/**
 * Renders a cabinet page the way `CompanyLayout` hosts it: the route path, the company in the outlet context. The page itself, its
 * mocked API and nothing else — not the app, not the network.
 */
export function renderCabinetPage(
  element: ReactElement,
  opts: { company: StaysCompanyManageDto; path: string; url: string; refresh?: () => void; client?: QueryClient },
) {
  const qc = opts.client ?? new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const refresh = opts.refresh ?? (() => undefined)
  const utils = render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[opts.url]}>
        <Routes>
          <Route element={<Outlet context={{ company: opts.company, refresh }} />}>
            <Route path={opts.path} element={element} />
          </Route>
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { ...utils, qc }
}
