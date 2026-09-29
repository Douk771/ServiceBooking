/**
 * `tel:` link for a phone the API stores as canonical digits (`79001234567`, no plus). The shared `telHref`
 * keeps only a leading «+» the input already had, so a bare `tel:79001234567` would be dialled as a national
 * number on some phones; a Russian canonical number is dialled as `+7…`.
 */
export function dialHref(phone: string | null | undefined): string {
  if (!phone) return ''
  const digits = phone.replace(/\D/g, '')
  if (!digits) return ''
  return `tel:+${digits}`
}
