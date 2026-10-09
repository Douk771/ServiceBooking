import { getNotificationErrorMessage } from '../../utils/notificationError'

/** The admin endpoints of cycle 40 answer 400/409 with a bare Russian line (text/plain): show it as it is. */
export function adminErrorText(err: unknown): string {
  const data = (err as { response?: { data?: unknown } })?.response?.data
  if (typeof data === 'string' && data.trim() !== '') return data
  return getNotificationErrorMessage(err)
}
