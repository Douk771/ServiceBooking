import { fmtDateTime } from '@/utils/dateFormat'
import type { ArrivalReminderSnapshotDto } from '../../types'

/**
 * «Напоминание о заезде» on the booking page (US-39-19, API_CONTRACT_CYCLE39.md §39.33.4): the SNAPSHOT of the text that went out
 * (not the current template), shown from the moment of sending even when no channel was reachable. The server leaves the link line out.
 */
export function ArrivalReminderBlock({ reminder }: { reminder: ArrivalReminderSnapshotDto | null | undefined }) {
  if (!reminder) return null
  return (
    <section className="rounded-2xl border border-line bg-white p-5" aria-labelledby="reminder-title" data-testid="arrival-reminder">
      <h2 id="reminder-title" className="mb-2 text-[15px] font-semibold text-ink">
        Напоминание о заезде
      </h2>
      <p className="whitespace-pre-line text-sm leading-relaxed text-ink">{reminder.text}</p>
      <p className="mt-2 text-xs text-muted">Отправлено {fmtDateTime(reminder.sentAtUtc)}</p>
    </section>
  )
}
