import { useCallback, useEffect, useMemo, useState } from 'react'
import { acceptPrices, cartStorageKey, parseCart, removeItem, upsertItem, type CartItem } from '../utils/cart'

function read(slug: string): CartItem[] {
  try {
    return parseCart(window.localStorage.getItem(cartStorageKey(slug)))
  } catch {
    return [] // storage blocked (private mode / policy) — the cart still works for this page view
  }
}

/**
 * One cart per shop in `localStorage` (`goods-cart:<slug>`, §395.3) — it survives reloads, the round trip
 * to /login for strict-mode shops, and is kept in sync between tabs.
 */
export function useCart(slug: string) {
  const [items, setItems] = useState<CartItem[]>(() => read(slug))

  useEffect(() => {
    setItems(read(slug))
  }, [slug])

  useEffect(() => {
    const onStorage = (e: StorageEvent) => {
      if (e.key === cartStorageKey(slug)) setItems(read(slug))
    }
    window.addEventListener('storage', onStorage)
    return () => window.removeEventListener('storage', onStorage)
  }, [slug])

  const commit = useCallback(
    (update: (prev: CartItem[]) => CartItem[]) => {
      setItems((prev) => {
        const next = update(prev)
        try {
          if (next.length === 0) window.localStorage.removeItem(cartStorageKey(slug))
          else window.localStorage.setItem(cartStorageKey(slug), JSON.stringify(next))
        } catch {
          // ignore quota/blocked storage
        }
        return next
      })
    },
    [slug],
  )

  const setLine = useCallback((item: CartItem) => commit((p) => upsertItem(p, item)), [commit])
  const remove = useCallback((productId: string) => commit((p) => removeItem(p, productId)), [commit])
  const clear = useCallback(() => commit(() => []), [commit])
  const confirmPrices = useCallback((current: Record<string, number>) => commit((p) => acceptPrices(p, current)), [commit])

  const byId = useMemo(() => new Map(items.map((i) => [i.productId, i])), [items])
  return { items, byId, setLine, remove, clear, confirmPrices }
}
