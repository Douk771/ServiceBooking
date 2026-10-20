import { Icon } from '../ui/Icon'
import { H3 } from './classes'
import { STEP_ICON } from './classes'
import type { LandingStepsPanel as Panel } from './types'

/** Панель «3 шага». headingLevel 3 — внутри секции (h3 + шаги h4), 2 — отдельная секция (h2 + шаги h3). */
export function LandingStepsPanel({ panel, headingLevel = 3 }: { panel: Panel; headingLevel?: 2 | 3 }) {
  const Title = headingLevel === 2 ? 'h2' : 'h3'
  const StepTitle = headingLevel === 2 ? 'h3' : 'h4'
  return (
    <div
      className={`${headingLevel === 2 ? '' : 'mt-8 md:mt-12 '}rounded-[28px] bg-cream-deep px-5 py-6 sm:px-8 sm:py-8 md:px-12 md:py-12`}
    >
      <Title id={panel.titleId} className={`${H3} mb-5 md:mb-8`}>
        {panel.title}
      </Title>
      <ol aria-labelledby={panel.titleId} className="list-none grid gap-5 md:grid-cols-3 md:gap-8">
        {panel.steps.map((s) => (
          <li key={s.title}>
            <div className="flex items-center gap-3">
              <div className={STEP_ICON}>
                <Icon name={s.icon} size={18} strokeWidth={1.6} className="text-gold-dark" aria-hidden />
              </div>
              <StepTitle className="font-serif text-[19px] md:text-[21px] leading-snug font-medium text-ink">{s.title}</StepTitle>
            </div>
            <p className="mt-2 text-[15px] leading-[1.55] text-ink-soft">{s.text}</p>
          </li>
        ))}
      </ol>
    </div>
  )
}
