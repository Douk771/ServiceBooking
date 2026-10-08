import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { CompanyLogoMark } from '../company/CompanyLogoMark'
import { Icon } from '../ui/Icon'

export interface CatalogCardProps {
  to: string
  name: string
  logoUrl?: string | null
  ariaLabel?: string
  testId?: string
  subtitle?: string
  badge?: ReactNode
  description?: string | null
  place?: string | null
  pill?: { text: string; tone: 'success' | 'info' | 'muted'; icon?: 'check' }
}

const PILL_TONE = {
  success: 'bg-success-bg text-success',
  info: 'bg-info-bg text-info',
  muted: 'bg-cream-deep text-muted',
} as const

/** Длинное слово без пробелов переносится по символам; названия и адреса не обрезаются (уточнение 8). */
const WRAP = 'break-words [overflow-wrap:anywhere]'

/**
 * Общая карточка каталога (салон / магазин), ARCHITECTURE_CYCLE37.md §37.5. Без `truncate` на названии и адресе:
 * ряд сетки растёт по самой высокой карточке (`h-full`).
 */
export function CatalogCard({ to, name, logoUrl, ariaLabel, testId, subtitle, badge, description, place, pill }: CatalogCardProps) {
  return (
    <Link
      to={to}
      aria-label={ariaLabel}
      data-testid={testId}
      className="block h-full bg-white border border-line rounded-[20px] p-[26px] transition-all duration-200 hover:shadow-card hover:-translate-y-[3px] hover:border-line-strong"
    >
      <div className="flex items-start gap-4 mb-4">
        <CompanyLogoMark size="catalog" name={name} logoUrl={logoUrl} />
        <div className="min-w-0 flex-1">
          <h3 className={`font-serif text-[19px] font-medium text-ink ${WRAP}`}>{name}</h3>
          {subtitle && <p className="text-sm text-muted mt-0.5">{subtitle}</p>}
          {badge}
        </div>
      </div>
      {description && <p className="text-sm leading-[1.55] text-ink-soft mb-4 line-clamp-2">{description}</p>}
      <div className="flex items-start justify-between gap-3 pt-3.5 border-t border-cream-deep">
        <div className="flex items-start gap-1.5 text-[13px] text-ink-soft min-w-0">
          {place && (
            <>
              <Icon name="map-pin" size={14} strokeWidth={1.6} className="shrink-0 mt-0.5" />
              <span className={`min-w-0 ${WRAP}`}>{place}</span>
            </>
          )}
        </div>
        {pill && (
          <span
            className={`inline-flex items-center gap-1 shrink-0 text-xs font-semibold px-2.5 py-1 rounded-full ${PILL_TONE[pill.tone]}`}
          >
            {pill.icon === 'check' && <Icon name="check" size={11} strokeWidth={2.2} />}
            {pill.text}
          </span>
        )}
      </div>
    </Link>
  )
}
