/**
 * The hold timer is counted on the SERVER's clock (ARCHITECTURE_CYCLE37.md §37.7.2): the phone's own clock may be minutes off, so
 * the difference between the answer's `serverTimeUtc` and the moment the answer arrived is subtracted from the phone's time.
 */
export function serverOffsetMs(serverTimeUtc: string, receivedAtMs: number): number {
  return Date.parse(serverTimeUtc) - receivedAtMs
}

/** Milliseconds left until `expiresAtUtc` on the server clock (negative once past). */
export function remainingMs(expiresAtUtc: string, nowMs: number, offsetMs: number): number {
  return Date.parse(expiresAtUtc) - (nowMs + offsetMs)
}

/** The server's current time, estimated from the phone's. */
export function serverNowMs(nowMs: number, offsetMs: number): number {
  return nowMs + offsetMs
}
