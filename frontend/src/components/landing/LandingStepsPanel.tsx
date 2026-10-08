import { Icon } from '../ui/Icon'
import { H3 } from './classes'
import type { LandingStepsPanel as Panel } from './types'

/** Панель «3 шага», общая для секций 3 и 4. */
export function LandingStepsPanel({ panel }: { panel: Panel }) {
  return (
    <div className="mt-12 md:mt-16 rounded-[28px] bg-cream-deep px-6 py-10 sm:px-10 md:px-12 md:py-14">
      <h3 id={panel.titleId} className={`${H3} mb-8 md:mb-10`}>
        {panel.title}
      </h3>
      <ol aria-labelledby={panel.titleId} className="list-none grid md:grid-cols-3 gap-10">
        {panel.steps.map((s) => (
          <li key={s.title}>
            <div className="w-[46px] h-[46px] rounded-full bg-cream border border-line-strong flex items-center justify-center mb-5">
              <Icon name={s.icon} size={20} strokeWidth={1.6} className="text-gold-dark" aria-hidden />
            </div>
            <h4 className="font-serif text-[21px] font-medium mb-2.5 text-ink">{s.title}</h4>
            <p className="text-[15px] leading-[1.6] text-ink-soft">{s.text}</p>
          </li>
        ))}
      </ol>
    </div>
  )
}
