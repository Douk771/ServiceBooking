import type { StayDisplayStatus } from '@/types/slots'
import { TONE_CLASSES, statusTone } from '@/utils/slots/slotStatus'

/** The status of a booking as the server worded it (`statusText`); the colour supports the words, never replaces them. */
export function StatusBadge({ status, text, className = '' }: { status: StayDisplayStatus; text: string; className?: string }) {
  return <span className={`inline-block rounded-full px-3 py-1 text-xs font-semibold ${TONE_CLASSES[statusTone(status)]} ${className}`}>{text}</span>
}
