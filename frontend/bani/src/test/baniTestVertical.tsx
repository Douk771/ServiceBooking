import type { ReactNode } from 'react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { SlotVerticalProvider, type SlotVertical } from '@/components/slots/SlotVerticalContext'
import type { SlotText } from '@/utils/slots/slotTexts'

/**
 * A bani vertical for component tests of FE-42-4 pages: the `api` is whatever the test mocks, the rest is the minimum the shared screens
 * read. It is deliberately NOT `bathsVertical` (the shell's file, FE-42-3): a page test must not depend on the shell's wiring.
 */
const note = (short: string): SlotText => ({ short, full: null })

export function testVertical(api: { publicServices?: object; orders?: object }): SlotVertical {
  return {
    kind: 'Baths',
    api: { publicServices: {}, orders: {}, cabinet: {}, sessions: {}, ...api } as unknown as SlotVertical['api'],
    paths: {
      resourcePage: (c, s) => `/${c}/${s}`,
      orderPage: (t) => `/s/${encodeURIComponent(t)}`,
      cabinetServices: (id) => `/cabinet/${id}/resources`,
      cabinetServiceNew: (id) => `/cabinet/${id}/resources/new`,
      cabinetService: (id, sid) => `/cabinet/${id}/resources/${sid}`,
      cabinetSession: (id, sid) => `/cabinet/${id}/service-sessions/${sid}`,
      cabinetDay: (id, d) => `/cabinet/${id}/service-day/${d}`,
      cabinetBooking: () => null,
    },
    legal: {
      keys: {
        serviceBookingNotice: 'BathBookingNotice',
        serviceBookingTerms: 'BathBookingTerms',
        serviceCancellationTerms: 'BathCancellationTerms',
        serviceCommentNotice: 'BathCommentNotice',
        messengerConsent: 'BathMessengerConsent',
        paymentProofNotice: 'BathPaymentProofNotice',
        serviceAddNotice: 'x',
        serviceSafetyOwnerNotice: 'x',
        serviceCancellationOwnerNotice: 'x',
        photoPeopleNotice: null,
        capacityOwnerNotice: null,
      },
      fallbacks: {
        BathBookingNotice: note('Уведомление о данных'),
        BathBookingTerms: note('Условия брони'),
        BathCancellationTerms: note('Условия отмены'),
        BathCommentNotice: note('Не указывайте лишнего'),
        BathMessengerConsent: note('Сообщать о брони в мессенджер'),
        BathPaymentProofNotice: note('Файл оплаты'),
      },
      fetchText: () => Promise.reject(new Error('no server text in tests')),
    },
    words: { brandTitle: 'EZBOOK Бани', brandName: 'EZBOOK Бани', chooseResource: 'Выбрать баню', notOrderingFallback: '', servicesEmptyText: '', stayBookingLabel: '' },
    features: { stayMode: false, capacity: true, houseBookingsToggle: false, photoPeopleNotice: true },
    NotFound: ({ title }) => <p>{title ?? 'Не найдено'}</p>,
    useCabinetCompany: () => ({ company: { id: 'c', myPermissions: [] }, refresh: () => undefined }),
  }
}

export function renderInVertical(vertical: SlotVertical, path: string, route: string, element: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <SlotVerticalProvider value={vertical}>
        <MemoryRouter initialEntries={[path]}>
          <Routes>
            <Route path={route} element={element} />
            <Route path="*" element={<p>other</p>} />
          </Routes>
        </MemoryRouter>
      </SlotVerticalProvider>
    </QueryClientProvider>,
  )
}
