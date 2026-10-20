import { STEPS_SECTION } from './classes'
import { LandingStepsPanel } from './LandingStepsPanel'
import type { LandingStepsPanel as Panel } from './types'

/** Отдельная секция «Как начать» (раскладка 'business-first'): h2 + шаги h3. */
export function LandingStepsSection({ panel }: { panel: Panel }) {
  return (
    <section aria-labelledby={panel.titleId} className={STEPS_SECTION}>
      <LandingStepsPanel panel={panel} headingLevel={2} />
    </section>
  )
}
