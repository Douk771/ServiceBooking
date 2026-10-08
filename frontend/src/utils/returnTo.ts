/**
 * `?returnTo=` for LoginPage/RegisterPage (ARCHITECTURE_CYCLE23.md §389.3-5): only a same-origin
 * relative path is honoured — it must start with a single `/`. `//host`, `/\host`, absolute URLs and
 * anything with control characters fall back to `fallback` (open-redirect guard).
 */
export function safeReturnTo(raw: string | null | undefined, fallback = '/'): string {
  if (!raw) return fallback
  if (!raw.startsWith('/')) return fallback
  // Any backslash is refused: react-router <= 7.17 and browsers treat `\\` like `/` (open redirect).
  if (raw.startsWith('//') || raw.includes('\\')) return fallback
  // eslint-disable-next-line no-control-regex
  if (/[\u0000-\u001f\u007f]/.test(raw)) return fallback
  return raw
}

/** Appends `?returnTo=<encoded>` to an auth link only when there is a safe one to carry over. */
export function withReturnTo(path: string, returnTo: string | null | undefined): string {
  const safe = safeReturnTo(returnTo, '')
  return safe ? `${path}?returnTo=${encodeURIComponent(safe)}` : path
}
