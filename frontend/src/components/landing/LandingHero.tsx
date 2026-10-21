import { FOCUS_RING } from './classes'
import { LandingActionLink } from './LandingActionLink'
import type { AnchorAction, LandingHeroConfig } from './types'

const HOW_TO_PLAIN =
  'inline-flex items-center min-h-[36px] text-sm text-ink-soft underline underline-offset-2 hover:no-underline'
const HOW_TO_WITH_CTA =
  'inline-flex items-center min-h-[44px] text-[15px] font-semibold text-ink underline underline-offset-4 hover:no-underline'

/** Секция 1: первый экран без картинки. `showClientLink: false` — каталог пуст, строке «Ищете, куда записаться?» вести некуда. */
export function LandingHero({
  config,
  showClientLink = true,
}: {
  config: Omit<LandingHeroConfig, 'howTo'> & { howTo?: AnchorAction }
  showClientLink?: boolean
}) {
  const { title, primaryAction, howTo, clientLink } = config
  return (
    <div className="max-w-[720px]">
      <p className="text-[13px] font-semibold tracking-[0.14em] uppercase text-gold-dark mb-4">{config.eyebrow}</p>
      <h1 className="font-serif text-[36px] sm:text-[48px] leading-[1.08] font-medium text-ink">
        {typeof title === 'string' ? (
          title
        ) : (
          <>
            {title.lead}
            <br />
            <em className="text-gold-dark italic">{title.accent}</em>
          </>
        )}
      </h1>
      <p className="mt-4 text-[16px] text-ink-soft">{config.intro}</p>
      {primaryAction ? (
        <div className="mt-6 flex flex-wrap items-center gap-x-5 gap-y-1">
          <LandingActionLink action={primaryAction} primary />
          {howTo && (
            <a href={howTo.href} className={HOW_TO_WITH_CTA}>
              {howTo.label}
            </a>
          )}
        </div>
      ) : (
        howTo && (
        <p className="mt-2">
          <a href={howTo.href} className={HOW_TO_PLAIN}>
            {howTo.label}
          </a>
        </p>
        )
      )}
      {clientLink && showClientLink && (
        <p className="mt-3 text-[14px] text-ink-soft">
          {clientLink.lead}{' '}
          <a
            href={clientLink.action.href}
            className={`inline-flex items-center min-h-[44px] font-semibold text-ink underline underline-offset-4 hover:no-underline ${FOCUS_RING}`}
          >
            {clientLink.action.label}
          </a>
        </p>
      )}
    </div>
  )
}
