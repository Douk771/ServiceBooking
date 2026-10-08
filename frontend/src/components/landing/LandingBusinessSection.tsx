import { Icon } from '../ui/Icon'
import { CHECK_ITEM, EYEBROW, H2, LEAD, SECTION } from './classes'
import { LandingActionLink } from './LandingActionLink'
import { LandingScreenshotFigure, LandingScreenshotPlaceholder } from './LandingScreenshot'
import { LandingStepsPanel } from './LandingStepsPanel'
import type { LandingBusinessConfig } from './types'

/** Секция 4: «Для бизнеса». Разметка — бывший goods BusinessBlock. */
export function LandingBusinessSection({ config, placeholder }: { config: LandingBusinessConfig; placeholder: boolean }) {
  const { screenshot } = config
  return (
    <section aria-labelledby={config.titleId} className={SECTION}>
      <p className={`${EYEBROW} mb-4`}>{config.eyebrow}</p>
      <div className="grid md:grid-cols-[1.05fr_0.95fr] gap-10 md:gap-16 items-start">
        <div>
          <h2 id={config.titleId} className={H2}>
            {config.title}
          </h2>
          <p className={LEAD}>{config.text}</p>
          <div className="mt-7 flex flex-wrap items-center gap-3">
            {config.actions.map((a, i) => (
              <LandingActionLink key={a.label} action={a} primary={i === 0} />
            ))}
          </div>
        </div>
        <ul className="space-y-4 md:pt-2">
          {config.benefits.map((b) => (
            <li key={b} className={CHECK_ITEM}>
              <Icon name="check" size={18} className="text-gold-dark shrink-0 mt-[3px]" aria-hidden />
              <span>{b}</span>
            </li>
          ))}
        </ul>
      </div>
      {screenshot ? (
        <LandingScreenshotFigure
          shot={screenshot}
          className="mt-12 md:mt-16"
          imgClassName="max-w-[320px] mx-auto md:max-w-none"
        />
      ) : (
        placeholder && (
          <div className="mt-12 md:mt-16">
            <LandingScreenshotPlaceholder />
          </div>
        )
      )}
      <LandingStepsPanel panel={config.stepsPanel} />
    </section>
  )
}
