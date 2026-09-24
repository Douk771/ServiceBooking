import { api } from './client'
import type { PhoneVerificationConfig, PhoneVerificationSessionCreated, PhoneVerificationSessionStatus } from '../types'

/**
 * API_CONTRACT_CYCLE14.md §162-§166 — platform-wide phone verification via the MAX bot (own
 * PhoneVerification subsystem, NOT a notification channel — ARCHITECTURE_CYCLE14.md §144.1).
 *
 * One `config` endpoint serves the registration form, the profile screen and the change-phone gate
 * alike (Q5) — every caller decides whether to show anything itself from the same `enabled/healthy`
 * pair, there is no second "is this available here" call.
 */
export const phoneVerificationApi = {
  getConfig: () => api.get<PhoneVerificationConfig>('/phone-verification/config').then((r) => r.data),

  /**
   * Anonymous during registration, authenticated (the client's request interceptor already attaches
   * the bearer token) on the profile screen — same call either way. `phone` can be omitted once
   * authenticated: the server falls back to the account's current number (§163).
   *
   * NOTE: no outbound call to MAX happens here — the bot only learns about the session once the
   * person opens the deep link themselves (§163, О2).
   */
  startSession: (phone?: string) =>
    api
      .post<PhoneVerificationSessionCreated>('/phone-verification/sessions', phone ? { phone } : {})
      .then((r) => r.data),

  /** Polled every `pollIntervalSeconds` by the caller (§164) — this function itself is a single GET. */
  getStatus: (sessionId: string, statusToken: string) =>
    api
      .get<PhoneVerificationSessionStatus>(`/phone-verification/sessions/${sessionId}`, {
        params: { statusToken },
      })
      .then((r) => r.data),

  /** Always 204, idempotent (§166) — callers treat this as fire-and-forget, never as a failure. */
  cancelSession: (sessionId: string, statusToken: string) =>
    api.delete<void>(`/phone-verification/sessions/${sessionId}`, { params: { statusToken } }),
}
