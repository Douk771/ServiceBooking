/** ARCHITECTURE_CYCLE29.md §29.8 — the one placeholder letter for a company without a (loadable) logo. */
export function companyInitial(name: string | null | undefined): string {
  const m = (name ?? '').match(/[\p{L}\p{N}]/u)
  if (!m) return ''
  const up = m[0].toLocaleUpperCase('ru-RU')
  return Array.from(up)[0] ?? ''
}
