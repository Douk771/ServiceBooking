import { Icon } from '../ui/Icon'
import { ACTIONS_ROW, CHECK_ITEM, DENSE_LIST, EYEBROW, H2, H3, LEAD, SECTION } from './classes'
import { LandingActionLink } from './LandingActionLink'
import { LandingScreenshotFigure, LandingScreenshotPlaceholder } from './LandingScreenshot'
import { LandingScreenshotCarousel } from './LandingScreenshotCarousel'
import { LandingStepsPanel } from './LandingStepsPanel'
import type { LandingClientsConfig, LandingClientsShowcaseConfig } from './types'

type Config = LandingClientsConfig | LandingClientsShowcaseConfig

/** Секция 3: «Для покупателей / клиентов». Разметка — бывший goods BuyersBlock. Кнопки и панель шагов — только если есть в конфиге. */
export function LandingClientsSection({ config, placeholder }: { config: Config; placeholder: boolean }) {
  const { screenshot, actions, stepsPanel } = config
  return (
    <section aria-labelledby={config.titleId} className={SECTION}>
      <p className={`${EYEBROW} mb-3`}>{config.eyebrow}</p>
      <div className="max-w-[720px]">
        <h2 id={config.titleId} tabIndex={-1} className={`${H2} scroll-mt-24 focus:outline-none`}>
          {config.title}
        </h2>
        <p className={LEAD}>{config.text}</p>
        {actions && (
          <div className={ACTIONS_ROW}>
            {actions.map((a, i) => (
              <LandingActionLink key={a.label} action={a} primary={i === 0} />
            ))}
          </div>
        )}
      </div>
      {stepsPanel && <LandingStepsPanel panel={stepsPanel} />}
      {screenshot && 'slides' in screenshot ? (
        <>
          <LandingScreenshotCarousel carousel={screenshot} className="mt-8 md:mt-12" />
          <div className="mt-8 md:mt-12">
            <h3 id={config.list.titleId} className={`${H3} mb-4`}>
              {config.list.title}
            </h3>
            <ul aria-labelledby={config.list.titleId} className={`${DENSE_LIST} sm:grid-cols-2`}>
              {config.list.items.map((t) => (
                <li key={t} className={CHECK_ITEM}>
                  <Icon name="check" size={18} className="text-gold-dark shrink-0 mt-[3px]" aria-hidden />
                  <span>{t}</span>
                </li>
              ))}
            </ul>
            {config.list.note && <p className="mt-3 text-[13px] text-muted">{config.list.note}</p>}
          </div>
        </>
      ) : (
        <div className="mt-8 md:mt-12 grid md:grid-cols-[1.05fr_0.95fr] gap-6 md:gap-16 items-start">
          <div>
            <h3 id={config.list.titleId} className={`${H3} mb-4`}>
              {config.list.title}
            </h3>
            <ul aria-labelledby={config.list.titleId} className={`${DENSE_LIST} sm:grid-cols-2 md:grid-cols-1`}>
              {config.list.items.map((t) => (
                <li key={t} className={CHECK_ITEM}>
                  <Icon name="check" size={18} className="text-gold-dark shrink-0 mt-[3px]" aria-hidden />
                  <span>{t}</span>
                </li>
              ))}
            </ul>
            {config.list.note && <p className="mt-3 text-[13px] text-muted">{config.list.note}</p>}
          </div>
          {screenshot ? (
            <LandingScreenshotFigure shot={screenshot} className="mx-auto w-full max-w-[320px] md:max-w-[340px]" />
          ) : (
            placeholder && <LandingScreenshotPlaceholder />
          )}
        </div>
      )}
    </section>
  )
}
