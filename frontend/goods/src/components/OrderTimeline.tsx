import { Icon } from '@/components/ui/Icon'
import { format } from 'date-fns'
import type { OrderTimelineStep } from '../types'

/** The four-step scale New → Accepted → Ready → Issued (§414.1); `reached` comes from the server. State is
 *  conveyed by text/icon, not by colour alone. */
export function OrderTimeline({ steps }: { steps: OrderTimelineStep[] }) {
  return (
    <ol className="flex flex-col sm:flex-row sm:items-start gap-3 sm:gap-0" aria-label="Ход выполнения заказа">
      {steps.map((s, i) => (
        <li key={s.status} className="flex sm:flex-col sm:flex-1 items-center sm:items-start gap-3 sm:gap-2 relative">
          <div className="flex items-center sm:w-full">
            <span
              className={`w-7 h-7 rounded-full flex items-center justify-center shrink-0 border-2 ${s.reached ? 'bg-ink border-ink text-cream' : 'bg-white border-line-strong text-transparent'}`}
              aria-hidden="true"
            >
              <Icon name="check" size={13} />
            </span>
            {i < steps.length - 1 && <span className={`hidden sm:block h-0.5 flex-1 mx-2 ${steps[i + 1].reached ? 'bg-ink' : 'bg-line'}`} aria-hidden="true" />}
          </div>
          <div>
            <p className={`text-sm ${s.reached ? 'font-semibold text-ink' : 'text-muted'}`}>
              {s.title}
              <span className="sr-only">{s.reached ? ' — достигнут' : ' — ещё нет'}</span>
            </p>
            {s.reachedAtUtc && <p className="text-xs text-muted">{format(new Date(s.reachedAtUtc), 'HH:mm')}</p>}
          </div>
        </li>
      ))}
    </ol>
  )
}
