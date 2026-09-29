import { useCallback, useEffect, useRef, useState } from 'react'
import { EMPTY_PICKUP_STATE, parsePickupState, pickupStorageKey, requestedDate, type PickupChoice, type PickupState } from '../utils/pickup'

function read(slug: string): PickupState {
  try {
    return parsePickupState(window.localStorage.getItem(pickupStorageKey(slug)))
  } catch {
    return EMPTY_PICKUP_STATE
  }
}

/**
 * The buyer's pick-up choice lives next to the cart (`goods-pickup:<slug>`, API_CONTRACT_CYCLE24.md §490): it survives a
 * reload and the round trip to /login, exactly like the items. Nothing here computes a time — only what the buyer picked.
 */
export function usePickupChoice(slug: string) {
  const [state, setState] = useState<PickupState>(() => read(slug))
  // The latest state, updated synchronously: two calls in one event handler (`choose(null)` then `browse(d)`) must compose.
  const ref = useRef(state)
  useEffect(() => {
    const fresh = read(slug)
    ref.current = fresh
    setState(fresh)
  }, [slug])

  const commit = useCallback(
    (next: PickupState) => {
      ref.current = next
      setState(next)
      try {
        if (!next.choice && !next.browseDate) window.localStorage.removeItem(pickupStorageKey(slug))
        else window.localStorage.setItem(pickupStorageKey(slug), JSON.stringify(next))
      } catch {
        // storage blocked — the choice still works for this page view
      }
    },
    [slug],
  )

  /** «Как можно скорее», a slot, or nothing. Choosing Asap returns the assortment to today; a slot pins the browsed date to its own. */
  const choose = useCallback(
    (choice: PickupChoice | null) => commit({ choice, browseDate: choice?.kind === 'Slot' ? choice.date : choice?.kind === 'Asap' ? null : ref.current.browseDate }),
    [commit],
  )
  const browse = useCallback(
    (date: string | null) => {
      const cur = ref.current.choice
      commit({ choice: cur?.kind === 'Slot' && cur.date !== date ? null : cur, browseDate: date })
    },
    [commit],
  )
  const reset = useCallback(() => commit(EMPTY_PICKUP_STATE), [commit])

  return { choice: state.choice, browseDate: state.browseDate, date: requestedDate(state), choose, browse, reset }
}
export type PickupControl = ReturnType<typeof usePickupChoice>
