import { describe, it, expect, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { SlotVerticalProvider, useSlotVertical, inVertical, type SlotVertical } from './SlotVerticalContext'
import { SlotNotice } from './ui/SlotNotice'

const vertical = (over: Partial<SlotVertical> = {}): SlotVertical => ({
  kind: 'Baths',
  api: {} as SlotVertical['api'],
  paths: {
    resourcePage: (c, s) => `/${c}/${s}`,
    orderPage: (t) => `/s/${t}`,
    cabinetServices: () => '',
    cabinetServiceNew: () => '',
    cabinetService: () => '',
    cabinetSession: () => '',
    cabinetDay: () => '',
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
      serviceAddNotice: 'BathAddNotice',
      serviceSafetyOwnerNotice: 'BathSafetyOwnerNotice',
      serviceCancellationOwnerNotice: 'BathCancellationOwnerNotice',
      photoPeopleNotice: '18-company-photo-people-notice',
      capacityOwnerNotice: 'BathCapacityOwnerNotice',
    },
    fallbacks: { BathBookingNotice: { short: '<p>Запасной текст бань</p>', full: null } },
    fetchText: () => Promise.reject(new Error('404')),
  },
  words: { brandTitle: 'EZBOOK Бани', brandName: 'EZBOOK Бани', chooseResource: 'К комплексу', notOrderingFallback: '', servicesEmptyText: '', stayBookingLabel: '' },
  features: { stayMode: false, capacity: true, houseBookingsToggle: false, photoPeopleNotice: true },
  NotFound: () => null,
  useCabinetCompany: () => ({ company: { id: 'c1', myPermissions: [] }, refresh: () => undefined }),
  ...over,
})

function Probe() {
  return <p>{useSlotVertical().words.brandTitle}</p>
}

describe('SlotVerticalProvider', () => {
  it('fails with a clear error when a shared screen runs outside any vertical', () => {
    const spy = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    expect(() => render(<Probe />)).toThrow(/SlotVerticalProvider/)
    spy.mockRestore()
  })

  it('gives the screen the words of its vertical, also through inVertical', () => {
    const Wrapped = inVertical(vertical(), Probe)
    render(<Wrapped />)
    expect(screen.getByText('EZBOOK Бани')).toBeInTheDocument()
  })
})

describe('SlotNotice in a vertical', () => {
  it('shows the vertical\'s own fallback when the server has no text yet', async () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
      <QueryClientProvider client={qc}>
        <SlotVerticalProvider value={vertical()}>
          <SlotNotice textKey="BathBookingNotice" />
        </SlotVerticalProvider>
      </QueryClientProvider>,
    )
    await waitFor(() => expect(screen.getByText('Запасной текст бань')).toBeInTheDocument())
  })
})
