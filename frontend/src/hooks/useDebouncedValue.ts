import { useEffect, useState } from 'react'

/**
 * Returns a copy of `value` that only updates `delayMs` after `value` stops changing.
 *
 * Used to avoid hitting the API on every keystroke when a search input drives a server-side
 * query (e.g. `GET /api/masters/clients?search=`) — see MasterClientsPage.tsx.
 */
export function useDebouncedValue<T>(value: T, delayMs = 400): T {
  const [debounced, setDebounced] = useState(value)

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs)
    return () => clearTimeout(timer)
  }, [value, delayMs])

  return debounced
}
