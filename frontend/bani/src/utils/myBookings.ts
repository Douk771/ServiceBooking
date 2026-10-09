/** `orderUrl` of «Мои брони» is a relative `/s/<token>`; anything else is not linked (the server owns the shape, the client does not trust it blindly). */
export function orderPathOf(orderUrl: string): string | null {
  return /^\/s\/[^/?#]+$/.test(orderUrl) ? orderUrl : null
}
