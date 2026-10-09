// GuestsCountRules.Validate (contracts/cycle42/bani-vectors.json `guests`, US-42-14): the client shows the 400 text before sending.
export interface GuestsCheck {
  ok: boolean
  /** Value the server stores: null when the service has no capacity (house services ignore the field). */
  stored: number | null
  error?: string
}

export function validateGuestsCount(guestsCount: number | null | undefined, capacity: number | null | undefined): GuestsCheck {
  if (capacity == null) return { ok: true, stored: null }
  if (guestsCount == null) return { ok: false, stored: null, error: 'Укажите, сколько человек придёт' }
  if (!Number.isInteger(guestsCount) || guestsCount < 1 || guestsCount > capacity) {
    return { ok: false, stored: null, error: `Число гостей — от 1 до ${capacity}` }
  }
  return { ok: true, stored: guestsCount }
}
