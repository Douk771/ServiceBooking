import { Icon } from '@/components/ui/Icon'
import type { SetupChecklistItemDto } from '../../types'

/** US-24-01 — «Чтобы начать принимать заказы:» — shown while any item is not done; the texts are the server's. */
export function SetupChecklist({ items }: { items: readonly SetupChecklistItemDto[] }) {
  if (items.length === 0 || items.every((i) => i.done)) return null
  return (
    <section aria-label="Чтобы начать принимать заказы" className="rounded-2xl border border-warning/40 bg-warning-bg px-5 py-4 mb-6" data-testid="setup-checklist">
      <p className="text-sm font-semibold text-warning">Чтобы начать принимать заказы:</p>
      <ul className="mt-2 flex flex-col gap-1.5">
        {items.map((i) => (
          <li key={i.code} className="text-sm flex items-center gap-2 text-ink">
            <Icon name={i.done ? 'check-circle' : 'alert-circle'} size={15} strokeWidth={1.8} className={i.done ? 'text-success' : 'text-warning'} />
            <span className={i.done ? 'line-through text-muted' : ''}>{i.text}</span>
            <span className="sr-only">{i.done ? ' — готово' : ' — не сделано'}</span>
          </li>
        ))}
      </ul>
    </section>
  )
}
