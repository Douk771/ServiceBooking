import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'

/**
 * Cycle 22 (ARCHITECTURE_CYCLE22.md §377) — the one home for the two display formats that used to
 * be re-declared as local `fmt`/`fmtDate` wrappers and inline `format(parseISO(…), …, { locale: ru })`
 * calls across pages. The value is an ISO string from the API; empty/absent renders as "—".
 */
export function fmtDate(value: string | null | undefined): string {
  return value ? format(parseISO(value), 'd MMM yyyy', { locale: ru }) : '—'
}

/** Same as {@link fmtDate}, plus the local time: "5 мар. 2026, 14:30". */
export function fmtDateTime(value: string | null | undefined): string {
  return value ? format(parseISO(value), 'd MMM yyyy, HH:mm', { locale: ru }) : '—'
}
