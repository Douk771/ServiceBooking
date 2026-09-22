import { create } from 'zustand'
import type { OwnerGate451 } from '../types'

/**
 * Owner-scope 451 (API_CONTRACT_CYCLE5.md §38.2, §42.2) — distinct from the global 451 tracked by
 * `legalStore`. Reading, and every non-owner action, keeps working; only the specific owner action
 * that hit the gate failed. The axios interceptor in `api/client.ts` fills this the instant any
 * `[RequiresOwnerTerms]` endpoint answers 451 with a JSON body, and `OwnerTermsGateModal` (mounted
 * once at the app root) reads it to open itself with the right document type/version.
 */
interface OwnerGateState {
  pending: OwnerGate451 | null
  setPending: (value: OwnerGate451 | null) => void
}

export const useOwnerGateStore = create<OwnerGateState>((set) => ({
  pending: null,
  setPending: (value) => set({ pending: value }),
}))
