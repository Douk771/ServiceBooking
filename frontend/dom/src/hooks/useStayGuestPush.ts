import { useSlotGuestPush, type SlotGuestPushOptions } from '@/hooks/slots/useSlotGuestPush'
import { guestBookingsApi } from '../api/guestBookings'
import { serviceOrdersApi } from '../api/serviceOrders'

/** The guest's browser push on dom: picks the routes of a booking (`/b/<token>`) or of a separate session (`/s/<token>`). */
export function useStayGuestPush(options: Omit<SlotGuestPushOptions, 'api'>) {
  return useSlotGuestPush({ ...options, api: options.kind === 'order' ? serviceOrdersApi : guestBookingsApi })
}
