import type { NotificationTransport } from '../../types'

/** Name of the query parameter that opens the connection wizard of a transport (cycle 43, ARCHITECTURE_CYCLE43.md §43.8.3). */
export const CONNECT_PARAM = 'connect'

/** `?connect=WhatsApp|Max` (any letter case) → the transport; anything else → `null`, the page opens as usual. */
export function transportFromConnectParam(raw: string | null): NotificationTransport | null {
  const v = (raw ?? '').trim().toLowerCase()
  if (v === 'whatsapp') return 'WhatsApp'
  if (v === 'max') return 'Max'
  return null
}
