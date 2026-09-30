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
  siteUrls: { services: 'https://demo.visit.ezbook.ru', orders: 'https://demo.zakaz.ezbook.ru' },
  ...over,
})

export const demoOrdersStatus = (over: Partial<DemoStatusDto> = {}): DemoStatusDto =>
  demoStatus({
    roles: [
      { role: 'shop-owner', label: 'Войти как владелец магазина' },
      { role: 'shop-staff', label: 'Войти как сотрудник магазина' },
      { role: 'shop-customer', label: 'Войти как покупатель' },
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
