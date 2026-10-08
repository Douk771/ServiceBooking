import { Link } from 'react-router-dom'
import { BTN_PRIMARY } from './classes'
import type { AnchorAction, LandingHeroConfig, RouteAction } from './types'

function HeroLink({ action, className }: { action: AnchorAction | RouteAction; className: string }) {
  return action.kind === 'anchor' ? (
    <a href={action.href} className={className}>
      {action.label}
    </a>
  ) : (
    <Link to={action.to} className={className}>
      {action.label}
    </Link>
  )
}

const HOW_TO_PLAIN =
  'inline-flex items-center min-h-[36px] text-sm text-ink-soft underline underline-offset-2 hover:no-underline'
const HOW_TO_WITH_CTA =
  'inline-flex items-center min-h-[36px] text-[15px] font-semibold text-ink underline underline-offset-4 hover:no-underline'

/** Секция 1: первый экран без картинки. */
export function LandingHero({ config }: { config: LandingHeroConfig }) {
  const { title, primaryAction, howTo } = config
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
        <div className="mt-6 flex flex-wrap items-center gap-x-6 gap-y-3">
          <HeroLink action={primaryAction} className={BTN_PRIMARY} />
          <a href={howTo.href} className={HOW_TO_WITH_CTA}>
            {howTo.label}
          </a>
        </div>
      ) : (
        <p className="mt-2">
          <a href={howTo.href} className={HOW_TO_PLAIN}>
            {howTo.label}
          </a>
        </p>
      )}
    </div>
  )
}
