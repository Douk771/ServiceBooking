import { useCallback, useEffect, useRef, useState } from 'react'
import { ordersApi } from '../api/orders'
import { applyBoardResponse, patchOrder, type BoardState } from '../utils/board'
import { createTicker } from '../utils/ticker'
import type { StaffOrderCardDto } from '../types'

export const BOARD_POLL_MS = 5000

/**
 * Board polling for the orders screen (§397.1): every 5 s from a worker ticker, immediately when the tab becomes
 * visible again, with `sinceRevision`/`businessDate` so an unchanged board costs one cheap request. Overlapping
 * polls are skipped. `lastOkAt` (client clock) drives the freshness indicator and the «Нет связи» banner.
 */
export function useOrderBoardPolling(shopId: string) {
  const [state, setState] = useState<BoardState | null>(null)
  const [lastOkAt, setLastOkAt] = useState<number | null>(null)
  const [error, setError] = useState<unknown>(null)
  const stateRef = useRef<BoardState | null>(null)
  const inflight = useRef(false)
  const alive = useRef(true)

  const poll = useCallback(
    async (full = false) => {
      if (inflight.current) return
      inflight.current = true
      try {
        const cur = full ? null : stateRef.current
        const res = await ordersApi.board(shopId, cur ? { sinceRevision: cur.revision, businessDate: cur.businessDate } : {})
        if (!alive.current) return
        const next = applyBoardResponse(stateRef.current, res)
        stateRef.current = next
        setState(next)
        setLastOkAt(Date.now())
        setError(null)
      } catch (e) {
        if (alive.current) setError(e)
      } finally {
        inflight.current = false
      }
    },
    [shopId],
  )

  useEffect(() => {
    alive.current = true
    stateRef.current = null
    setState(null)
    setLastOkAt(null)
    setError(null)
    void poll(true)
    const stop = createTicker(BOARD_POLL_MS, () => void poll())
    const onVisible = () => {
      if (document.visibilityState === 'visible') void poll()
    }
    document.addEventListener('visibilitychange', onVisible)
    return () => {
      alive.current = false
      stop()
      document.removeEventListener('visibilitychange', onVisible)
    }
  }, [shopId, poll])

  /** A fresh card (action answer or 409 body) goes in right away; the next poll confirms it. */
  const patch = useCallback((order: StaffOrderCardDto) => {
    if (!stateRef.current) return
    const next = patchOrder(stateRef.current, order)
    stateRef.current = next
    setState(next)
  }, [])

  return { state, lastOkAt, error, refresh: () => poll(true), patch }
}
