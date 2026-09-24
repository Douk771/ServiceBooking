import { format } from 'date-fns'
import { ru } from 'date-fns/locale'
import { Icon } from '../ui/Icon'

interface PhoneVerifiedBadgeProps {
  /** ISO date-time, or omit/null when the caller only wants the label without a date (e.g. the staff client list, §170.2). */
  verifiedAtUtc?: string | null
  label?: string
  className?: string
}

/**
 * §5 "Доступность" / US-12-14, US-12-15 — a confirmed phone is announced with a TEXT label, never by
 * colour or a bare icon alone. Used on the profile page (with a date) and on the staff client list
 * (label only, R15 — no colour-only signal there either, and it never renders for `false`/`null`).
 */
export function PhoneVerifiedBadge({ verifiedAtUtc, label = 'Номер подтверждён', className = '' }: PhoneVerifiedBadgeProps) {
  const dateLabel = verifiedAtUtc ? format(new Date(verifiedAtUtc), 'd MMM yyyy', { locale: ru }) : null

  return (
    <span
      className={`inline-flex items-center gap-1.5 text-xs font-semibold text-success bg-success-bg px-2.5 py-1 rounded-full ${className}`}
    >
      <Icon name="check-circle" size={14} strokeWidth={1.8} />
      {label}
      {dateLabel ? ` · ${dateLabel}` : ''}
    </span>
  )
}
