import type { ReactElement } from 'react'
import { CONTAINER, FOCUS_RING } from './classes'
import { Icon } from '../ui/Icon'
import { LandingHeroBackdrop } from './LandingHeroBackdrop'
import type { LandingPanelHeroConfig } from './types'

/** Секция 1, вариант «с панелью»: полоса во всю ширину, текст слева, слот формы справа (ARCHITECTURE_CYCLE41.md §41.3.3). */
export function LandingPanelHero({ config, aside }: { config: LandingPanelHeroConfig; aside?: ReactElement }) {
  const { title, howTo, facts, backdrop } = config
  const grid = aside ? 'grid gap-8 lg:grid-cols-[1.05fr_1fr] lg:items-center' : 'grid gap-8'
  return (
    <div className="relative overflow-hidden border-b border-line bg-gradient-to-b from-cream-deep to-cream">
      {backdrop !== 'none' && <LandingHeroBackdrop kind={backdrop} />}
      <div className={`relative ${CONTAINER} pt-10 md:pt-16 pb-20 md:pb-28 ${grid}`}>
        <div>
          <p className="inline-flex items-center rounded-full border border-line bg-white px-3 py-1 mb-4 text-[13px] font-semibold tracking-[0.14em] uppercase text-gold-dark">
            {config.eyebrow}
          </p>
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
          <p className="mt-4 max-w-[520px] text-[16px] leading-relaxed text-ink-soft">{config.intro}</p>
          <p className="mt-2">
            <a
              href={howTo.href}
              className={`inline-flex items-center min-h-[44px] text-[15px] font-semibold text-ink underline underline-offset-4 hover:no-underline ${FOCUS_RING}`}
            >
              {howTo.label}
            </a>
          </p>
          {facts.length > 0 && (
            <ul role="list" className="mt-4 flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:gap-x-6">
              {facts.map((f) => (
                <li key={f.text} className="flex items-center gap-2 text-[14px] text-ink">
                  <Icon name={f.icon} size={16} strokeWidth={1.6} className="shrink-0 text-gold-dark" aria-hidden="true" />
                  {f.text}
                </li>
              ))}
            </ul>
          )}
        </div>
        {aside && <div className="rounded-3xl border border-line bg-white p-5 shadow-soft sm:p-6">{aside}</div>}
      </div>
    </div>
  )
}
