import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { catalogApi } from '../../api/catalog'
import { Button } from '@/components/ui/Button'
import { formatQuantity } from '../../utils/quantityFormat'
import { getCatalogErrorMessage } from '../../utils/catalogError'
import { parseStockInput, stockToInput } from '../../utils/stockInput'
import type { ProductDto } from '../../types'

/**
 * US-23-17 — «на складе» / «в резерве» / свободно; staff and owner may correct the stock. Empty input means
 * «не учитывать» (the product sells without a limit). A reserve above the stock is allowed but flagged (§410.5).
 */
export function StockEditor({ shopId, product, onChanged }: { shopId: string; product: ProductDto; onChanged: (p: ProductDto) => void }) {
  const [editing, setEditing] = useState(false)
  const [text, setText] = useState('')
  const [error, setError] = useState('')
  const { onHand, reserved, free } = product.stock
  const unit = product.unit

  const save = useMutation({
    mutationFn: (value: number | null) => catalogApi.setStock(shopId, product.id, value),
    onSuccess: (p) => {
      onChanged(p)
      setEditing(false)
    },
    onError: (e) => setError(getCatalogErrorMessage(e, 'Не удалось сохранить остаток.')),
  })

  const submit = () => {
    const parsed = parseStockInput(text, unit)
    if ('error' in parsed) {
      setError(parsed.error)
      return
    }
    setError('')
    save.mutate(parsed.value)
  }

  if (!editing) {
    return (
      <div className="text-xs text-ink-soft flex flex-wrap items-center gap-x-3 gap-y-1">
        {onHand === null ? (
          <span>Остаток не учитывается</span>
        ) : (
          <>
            <span>На складе: <b className="text-ink">{formatQuantity(unit, onHand)}</b></span>
            <span>В резерве: <b className="text-ink">{formatQuantity(unit, reserved)}</b></span>
            {free !== null && free < 0 && (
              <span role="alert" className="text-danger font-semibold">
                Резерв больше остатка на {formatQuantity(unit, -free)}
              </span>
            )}
          </>
        )}
        <button
          type="button"
          className="text-gold hover:text-gold-dark font-semibold"
          onClick={() => {
            setText(stockToInput(onHand, unit))
            setError('')
            setEditing(true)
          }}
        >
          Изменить остаток
        </button>
      </div>
    )
  }

  return (
    <form
      className="flex flex-wrap items-start gap-2"
      onSubmit={(e) => {
        e.preventDefault()
        submit()
      }}
    >
      <div>
        <label className="sr-only" htmlFor={`stock-${product.id}`}>
          Остаток: {product.name}
        </label>
        <input
          id={`stock-${product.id}`}
          inputMode={unit === 'Weight' ? 'decimal' : 'numeric'}
          value={text}
          onChange={(e) => setText(e.target.value)}
          placeholder={unit === 'Weight' ? 'кг, пусто — не учитывать' : 'шт, пусто — не учитывать'}
          className="w-52 rounded-lg border border-line px-3 py-1.5 text-sm bg-white outline-none focus:border-gold"
          autoFocus
        />
        <p className="text-[11px] text-muted mt-1">{unit === 'Weight' ? 'В килограммах, с точностью до грамма.' : 'В штуках.'}</p>
        {error && <p role="alert" className="text-xs text-danger mt-1">{error}</p>}
      </div>
      <Button type="submit" size="sm" loading={save.isPending}>
        Сохранить
      </Button>
      <Button type="button" size="sm" variant="secondary" onClick={() => setEditing(false)} disabled={save.isPending}>
        Отмена
      </Button>
    </form>
  )
}
