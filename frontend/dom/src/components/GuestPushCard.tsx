import { GuestPushCard as SharedGuestPushCard } from '@/components/slots/ui/GuestPushCard'
import { guestBookingsApi } from '../api/guestBookings'
import { serviceOrdersApi } from '../api/serviceOrders'

/** The guest's browser push on dom: a booking (`/b/<token>`) or a separate session (`/s/<token>`) picks its own routes. */
export function GuestPushCard({ token, info, kind = 'booking' }: { token: string; info: { available: boolean; publicKey?: string | null }; kind?: 'booking' | 'order' }) {
  return <SharedGuestPushCard token={token} info={info} kind={kind} api={kind === 'order' ? serviceOrdersApi : guestBookingsApi} />
}
