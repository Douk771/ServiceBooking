import { Icon } from '../ui/Icon'
import { CHECK_ITEM, EYEBROW, H2, H3, LEAD, SECTION } from './classes'
import { LandingActionLink } from './LandingActionLink'
import { LandingScreenshotFigure, LandingScreenshotPlaceholder } from './LandingScreenshot'
import { LandingStepsPanel } from './LandingStepsPanel'
import type { LandingClientsConfig } from './types'

/** Секция 3: «Для покупателей / клиентов». Разметка — бывший goods BuyersBlock. */
export function LandingClientsSection({ config, placeholder }: { config: LandingClientsConfig; placeholder: boolean }) {
  const { screenshot } = config
  return (
    <section aria-labelledby={config.titleId} className={SECTION}>
      <p className={`${EYEBROW} mb-4`}>{config.eyebrow}</p>
      <div className="max-w-[720px]">
        <h2 id={config.titleId} tabIndex={-1} className={`${H2} scroll-mt-24 focus:outline-none`}>
          {config.title}
        </h2>
        <p className={LEAD}>{config.text}</p>
        <div className="mt-7 flex flex-wrap items-center gap-3">
          {config.actions.map((a, i) => (
            <LandingActionLink key={a.label} action={a} primary={i === 0} />
          ))}
        </div>
      </div>
      <LandingStepsPanel panel={config.stepsPanel} />
      <div className="mt-12 md:mt-16 grid md:grid-cols-[1.05fr_0.95fr] gap-10 md:gap-16 items-start">
        <div>
          <h3 id={config.list.titleId} className={`${H3} mb-6`}>
            {config.list.title}
          </h3>
          <ul aria-labelledby={config.list.titleId} className="space-y-4">
            {config.list.items.map((t) => (
              <li key={t} className={CHECK_ITEM}>
                <Icon name="check" size={18} className="text-gold-dark shrink-0 mt-[3px]" aria-hidden />
                <span>{t}</span>
              </li>
            ))}
          </ul>
          {config.list.note && <p className="mt-6 text-[13px] text-muted">{config.list.note}</p>}
        </div>
        {screenshot ? (
          <LandingScreenshotFigure shot={screenshot} className="mx-auto w-full max-w-[320px] md:max-w-[340px]" />
        ) : (
          placeholder && <LandingScreenshotPlaceholder />
        )}
      </div>
    </section>
  )
}
