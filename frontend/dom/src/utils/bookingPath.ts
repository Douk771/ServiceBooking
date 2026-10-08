/** Path of a booking page from the absolute `bookingUrl`: same-origin only, otherwise the link is not followed (the token is the access). */
export function bookingPathOf(bookingUrl: string): string | null {
  try {
    const u = new URL(bookingUrl, window.location.origin)
    return u.origin === window.location.origin && u.pathname.startsWith('/b/') ? u.pathname : null
  } catch {
    return null
  }
}
