import type { ReactElement } from 'react'
import { render } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { DemoStatusDto } from '../../api/demo'

export const demoStatus = (over: Partial<DemoStatusDto> = {}): DemoStatusDto => ({
  demoMode: true,
  resetting: false,
  resetLocalTime: '04:00',
  timeZoneId: 'Europe/Moscow',
  lastResetAtUtc: null,
  roles: [
    { role: 'owner', label: 'Войти как владелец салона' },
    { role: 'master', label: 'Войти как мастер' },
    { role: 'client', label: 'Войти как клиент' },
  ],
  ...over,
})

export function renderWithProviders(
  ui: ReactElement,
  qc = new QueryClient({ defaultOptions: { queries: { retry: false } } }),
) {
  return {
    qc,
    ...render(
      <QueryClientProvider client={qc}>
        <MemoryRouter>{ui}</MemoryRouter>
      </QueryClientProvider>,
    ),
  }
}
