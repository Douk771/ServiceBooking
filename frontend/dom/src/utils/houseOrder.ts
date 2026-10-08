/** Order of the houses on the company page = the owner's order of the non-archived ones; one step, then the FULL list is sent (API_CONTRACT_CYCLE37.md §37.28). */
export function moveHouse(ids: string[], index: number, direction: -1 | 1): string[] | null {
  const target = index + direction
  if (index < 0 || index >= ids.length || target < 0 || target >= ids.length) return null
  const next = [...ids]
  ;[next[index], next[target]] = [next[target], next[index]]
  return next
}
