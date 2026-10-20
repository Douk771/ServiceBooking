import { Icon } from '../ui/Icon'
import { ACTIONS_ROW, CHECK_ITEM, DENSE_LIST, EYEBROW, H2, LEAD, SECTION } from './classes'
import { LandingActionLink } from './LandingActionLink'
import { LandingScreenshotFigure, LandingScreenshotPlaceholder } from './LandingScreenshot'
import { LandingStepsPanel } from './LandingStepsPanel'
import type { LandingBusinessConfig } from './types'

/** Секция 4: «Для бизнеса». Разметка — бывший goods BusinessBlock. */
export function LandingBusinessSection({
  config,
  placeholder,
  steps = 'inside',
}: {
  config: LandingBusinessConfig
  placeholder: boolean
  steps?: 'inside' | 'none'
}) {
  const { screenshot } = config
  return (
    <section aria-labelledby={config.titleId} className={SECTION}>
      <p className={`${EYEBROW} mb-3`}>{config.eyebrow}</p>
      <div className="grid md:grid-cols-[1.05fr_0.95fr] gap-6 md:gap-16 items-start">
        <div>
          <h2 id={config.titleId} className={H2}>
            {config.title}
          </h2>
          <p className={LEAD}>{config.text}</p>
          <div className={ACTIONS_ROW}>
            {config.actions.map((a, i) => (
              <LandingActionLink key={a.label} action={a} primary={i === 0} />
            ))}
          </div>
        </div>
        <ul className={`${DENSE_LIST} sm:grid-cols-2 md:grid-cols-1 md:pt-2`}>
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
          className="mt-8 md:mt-12"
          imgClassName="max-w-[320px] mx-auto md:max-w-none max-md:!w-auto max-md:!h-[min(60vh,540px)]"
        />
      ) : (
        placeholder && (
          <div className="mt-8 md:mt-12">
            <LandingScreenshotPlaceholder />
          </div>
        )
      )}
      {steps === 'inside' && <LandingStepsPanel panel={config.stepsPanel} />}
    </section>
  )
}
