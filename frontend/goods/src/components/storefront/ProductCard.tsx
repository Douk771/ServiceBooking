import { Icon } from '@/components/ui/Icon'
import { formatQuantity, formatUnitPrice } from '../../utils/quantityFormat'
import { decrementQuantity, incrementQuantity, quantityRule } from '../../utils/cart'
import type { StorefrontProductDto } from '../../types'

interface Props {
  product: StorefrontProductDto
  quantity: number | undefined
  onChange: (quantity: number | null) => void
}

/** US-23-18 — photo, name, portion or price per kg, description, «Состав и аллергены»; unavailable goods stay
 *  on the page greyed out with «Закончилось» and cannot be added. `available` comes from the server. */
export function ProductCard({ product: p, quantity, onChange }: Props) {
  const rule = quantityRule(p)
  const inCart = quantity !== undefined && quantity > 0

  return (
    <li className={`rounded-2xl border bg-white p-4 flex gap-4 ${p.available ? 'border-line' : 'border-line opacity-70'}`} data-testid="product-card">
      {p.thumbnailUrl || p.imageUrl ? (
        <img src={p.thumbnailUrl ?? p.imageUrl ?? ''} alt="" loading="lazy" className={`w-24 h-24 sm:w-28 sm:h-28 rounded-xl object-cover shrink-0 ${p.available ? '' : 'grayscale'}`} />
      ) : (
        <span className="w-24 h-24 sm:w-28 sm:h-28 rounded-xl bg-cream-deep flex items-center justify-center text-muted shrink-0">
          <Icon name="image" size={26} strokeWidth={1.3} />
        </span>
      )}
      <div className="min-w-0 flex-1 flex flex-col">
        <h3 className="font-semibold text-ink leading-snug">{p.name}</h3>
        {p.portionText && <p className="text-xs text-muted mt-0.5">{p.portionText}</p>}
        {p.description && <p className="text-sm text-ink-soft mt-1 line-clamp-3">{p.description}</p>}
        {p.foodInfo.compositionAndAllergens && (
          <details className="mt-1.5 text-xs text-ink-soft">
            <summary className="cursor-pointer text-gold hover:text-gold-dark">Состав и аллергены</summary>
            <p className="mt-1 whitespace-pre-line">{p.foodInfo.compositionAndAllergens}</p>
          </details>
        )}

        <div className="mt-auto pt-3 flex items-center justify-between gap-3 flex-wrap">
          <div>
            <p className="font-serif text-lg text-ink">{formatUnitPrice(p.unit, p.price)}</p>
            {p.unit === 'Weight' && <p className="text-[11px] text-muted">шаг {formatQuantity('Weight', rule.step)}, от {formatQuantity('Weight', rule.min)}</p>}
          </div>

          {!p.available ? (
            <span className="text-xs font-semibold px-3 py-1.5 rounded-full bg-cream-deep text-muted">Закончилось</span>
          ) : inCart ? (
            <div className="inline-flex items-center rounded-full border border-line bg-cream" role="group" aria-label={`Количество: ${p.name}`}>
              <button
                type="button"
                aria-label={`Уменьшить: ${p.name}`}
                className="w-10 h-10 flex items-center justify-center text-ink hover:bg-cream-deep rounded-l-full"
                onClick={() => onChange(decrementQuantity(rule, quantity!))}
              >
                <Icon name="minus" size={16} />
              </button>
              <span className="min-w-[64px] text-center text-sm font-semibold text-ink" aria-live="polite">
                {formatQuantity(p.unit, quantity!)}
              </span>
              <button
                type="button"
                aria-label={`Добавить ещё: ${p.name}`}
                disabled={quantity! >= rule.max}
                className="w-10 h-10 flex items-center justify-center text-ink hover:bg-cream-deep rounded-r-full disabled:opacity-40"
                onClick={() => onChange(incrementQuantity(rule, quantity))}
              >
                <Icon name="plus" size={16} />
              </button>
            </div>
          ) : (
            <button
              type="button"
              className="inline-flex items-center gap-1.5 rounded-full bg-ink text-cream text-sm font-semibold px-5 py-2.5 hover:bg-ink/90 transition-colors"
              onClick={() => onChange(incrementQuantity(rule, undefined))}
            >
              <Icon name="plus" size={15} /> В корзину
            </button>
          )}
        </div>
      </div>
    </li>
  )
}
