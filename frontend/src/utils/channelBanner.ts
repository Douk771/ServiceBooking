import type { ChannelState } from '../types'

const BROKEN_STATES: ChannelState[] = ['Disconnected', 'Blocked', 'NeedsReconnect']

export type ChannelBannerKind = 'broken' | 'idle' | null

/**
 * Decides which of the two banner texts (broken connection vs. upcoming idle-deletion, US-62 п. 2 и
 * п. 6) applies to a channel — pulled out of the component so the branching is unit-testable without
 * rendering. A channel can be idle while still `Connected` (no active company assigned yet says
 * nothing about the WhatsApp session itself), so "broken" always wins when both are true.
 */
export function getChannelBannerKind(state: ChannelState, idleDeadline: string | null | undefined): ChannelBannerKind {
  if (BROKEN_STATES.includes(state)) return 'broken'
  if (idleDeadline) return 'idle'
  return null
}
