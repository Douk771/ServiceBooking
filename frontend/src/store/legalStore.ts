import { create } from 'zustand'

/**
 * Not persisted (unlike authStore) — this is a same-session signal, not user data. Set by the 451
 * branch in src/api/client.ts (ARCHITECTURE.md §0.3) the instant any authenticated call comes back
 * blocked, so ConsentGate can render before the next `consent-status` poll would have caught up.
 * Cleared on login/register (fresh token, fresh claims) and after POST /api/legal/accept succeeds.
 */
interface LegalState {
  consentRequired: boolean
  setConsentRequired: (value: boolean) => void
}

export const useLegalStore = create<LegalState>((set) => ({
  consentRequired: false,
  setConsentRequired: (value) => set({ consentRequired: value }),
}))
