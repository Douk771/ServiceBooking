import { useState, type MouseEvent } from 'react'
import { Icon } from '@/components/ui/Icon'
import { applyLegalRuntimeValues } from '@/utils/legalRuntimeValues'
import { useStayText } from '../hooks/useStayText'
import type { StayTextKey } from '../utils/stayTexts'

interface Props {
  textKey: StayTextKey
  /** `box` — a tinted hint block with an icon (owner hints, warnings); `plain` — small muted text (under a button). */
  variant?: 'box' | 'plain'
  /** Substituted into `data-legal-value="companyName"` of the text. */
  companyName?: string | null
  /** The text may link to `#stay-terms` (conditions of booking); the page opens them instead of jumping nowhere. */
  onOpenTerms?: () => void
  className?: string
  id?: string
}

/**
 * One legal microcopy of the vertical: the lawyer's text or its fallback (`useStayText`), always with the company name
 * substituted per render (the cached text is shared between companies). A text with a «Полный текст» part gets «Подробнее».
 */
export function StayNotice({ textKey, variant = 'box', companyName, onOpenTerms, className = '', id }: Props) {
  const text = useStayText(textKey)
  const [expanded, setExpanded] = useState(false)
  const render = (html: string) => applyLegalRuntimeValues(html, { companyName: companyName ?? null })

  const onClick = (e: MouseEvent<HTMLDivElement>) => {
    const a = (e.target as HTMLElement).closest('a')
    const href = a?.getAttribute('href')
    if (a && (href === '#stay-terms' || href === '#service-terms') && onOpenTerms) {
      e.preventDefault()
      onOpenTerms()
    }
  }

  const body = (
    <>
      <div
        className="[&_p]:mb-1.5 [&_p:last-child]:mb-0 [&_a]:underline [&_a]:text-gold-dark"
        onClick={onClick}
        dangerouslySetInnerHTML={{ __html: render(text.short) }}
      />
      {text.full && (
        <>
          <button
            type="button"
            className="mt-1.5 inline-flex min-h-[32px] items-center gap-1 text-xs font-semibold text-gold-dark hover:underline"
            aria-expanded={expanded}
            onClick={() => setExpanded((v) => !v)}
          >
            {expanded ? 'Свернуть' : 'Подробнее'}
          </button>
          {expanded && (
            <div
              className="mt-2 [&_p]:mb-1.5 [&_p:last-child]:mb-0 [&_a]:underline [&_a]:text-gold-dark"
              onClick={onClick}
              dangerouslySetInnerHTML={{ __html: render(text.full) }}
            />
          )}
        </>
      )}
    </>
  )

  if (variant === 'plain') {
    return (
      <div id={id} className={`text-xs leading-relaxed text-muted ${className}`}>
        {body}
      </div>
    )
  }
  return (
    <div id={id} className={`flex items-start gap-2 rounded-xl bg-cream-deep px-3 py-2.5 text-xs leading-relaxed text-ink-soft ${className}`}>
      <Icon name="alert-circle" size={14} strokeWidth={1.8} className="mt-0.5 shrink-0 text-gold-dark" />
      <div className="min-w-0">{body}</div>
    </div>
  )
}
