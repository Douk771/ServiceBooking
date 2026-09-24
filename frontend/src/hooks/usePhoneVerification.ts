import { useCallback, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { phoneVerificationApi } from '../api/phoneVerification'
import { getPhoneVerificationErrorMessage } from '../utils/phoneVerificationError'
import type { PhoneVerificationConfig, PhoneVerificationSessionCreated, PhoneVerificationSessionStatus, PhoneVerificationStatus } from '../types'

// ARCHITECTURE_CYCLE14.md §148.4 (Q12, П9) — a session in one of these statuses is done; the poller
// stops itself, matching the server's own rule (Pending/Linked keep going, everything else is final).
const TERMINAL_STATUSES: ReadonlySet<PhoneVerificationStatus> = new Set(['Verified', 'Rejected', 'Cancelled', 'Expired'])

export function isTerminalStatus(status: PhoneVerificationStatus | undefined): boolean {
  return !!status && TERMINAL_STATUSES.has(status)
}

const DEFAULT_POLL_MS = 2000

/**
 * `GET /api/phone-verification/config` (§162) — the single source of truth for "show the button at
 * all". Shared by the registration form, the profile screen and the change-phone gate (Q5); cached
 * for 5 minutes so none of them re-fetches on every mount.
 */
export function usePhoneVerificationConfig() {
  return useQuery<PhoneVerificationConfig>({
    queryKey: ['phone-verification-config'],
    queryFn: phoneVerificationApi.getConfig,
    staleTime: 5 * 60 * 1000,
  })
}

export interface UsePhoneVerificationResult {
  session: PhoneVerificationSessionCreated | null
  status: PhoneVerificationSessionStatus | null
  /** True while a session exists and hasn't reached a terminal status yet. */
  isPolling: boolean
  isStarting: boolean
  startError: string | null
  /** Set once the status poll itself fails (e.g. 404 — session expired/reaped server-side, wrong
   * token). The poller stops itself in this case (see `isPolling`) instead of hammering the endpoint
   * forever; the caller is expected to surface this next to the dialog/button. */
  statusError: string | null
  /** `phone` — canonical digits; omit to let the server use the signed-in account's current number. */
  start: (phone?: string) => Promise<void>
  /** Fire-and-forget per the contract (§166) — always resolves, clears local state regardless. */
  cancel: () => Promise<void>
  /**
   * Call whenever the phone field the caller is tracking changes value. If an open session was
   * started for a DIFFERENT number, it's cancelled automatically (US-14-05, R8: "the user edited the
   * number after asking to verify it" must not leave a stale session presentable at submit time).
   * The server does not rely on this — it re-checks the session's own phone independently at
   * consumption time (§148.5) — this is purely so the UI doesn't keep offering a QR/link that can
   * never match what's now in the form.
   */
  syncPhone: (currentPhone: string) => void
}

export function usePhoneVerification(pollIntervalMs = DEFAULT_POLL_MS): UsePhoneVerificationResult {
  const [session, setSession] = useState<PhoneVerificationSessionCreated | null>(null)
  const [sessionPhone, setSessionPhone] = useState<string | null>(null)
  const [isStarting, setIsStarting] = useState(false)
  const [startError, setStartError] = useState<string | null>(null)

  const statusQuery = useQuery<PhoneVerificationSessionStatus>({
    queryKey: ['phone-verification-session', session?.sessionId],
    queryFn: () => phoneVerificationApi.getStatus(session!.sessionId, session!.statusToken),
    enabled: !!session,
    retry: false,
    // Stop polling once the status is terminal — OR once the poll itself is erroring (e.g. 404: the
    // session was reaped/expired server-side, or the token is wrong). Without the error branch this
    // hit `pollIntervalMs` forever, since `query.state.data` never gets set on a failed request.
    refetchInterval: (query) => (isTerminalStatus(query.state.data?.status) || query.state.error ? false : pollIntervalMs),
    refetchIntervalInBackground: false,
  })

  const start = useCallback(async (phone?: string) => {
    setStartError(null)
    setIsStarting(true)
    try {
      const created = await phoneVerificationApi.startSession(phone)
      setSession(created)
      setSessionPhone(phone ?? '')
    } catch (err) {
      setStartError(getPhoneVerificationErrorMessage(err))
    } finally {
      setIsStarting(false)
    }
  }, [])

  const cancel = useCallback(async () => {
    const current = session
    setSession(null)
    setSessionPhone(null)
    setStartError(null)
    if (!current) return
    try {
      await phoneVerificationApi.cancelSession(current.sessionId, current.statusToken)
    } catch {
      // Best-effort (§166 is idempotent server-side; a network hiccup here just leaves an
      // already-abandoned session to expire on its own TTL).
    }
  }, [session])

  const syncPhone = useCallback(
    (currentPhone: string) => {
      if (session && sessionPhone !== null && sessionPhone !== currentPhone) {
        void cancel()
      }
    },
    [session, sessionPhone, cancel],
  )

  return {
    session,
    status: statusQuery.data ?? null,
    isPolling: !!session && !isTerminalStatus(statusQuery.data?.status) && !statusQuery.isError,
    isStarting,
    startError,
    statusError: statusQuery.isError
      ? getPhoneVerificationErrorMessage(statusQuery.error, 'Не удалось получить статус подтверждения. Попробуйте получить новую ссылку.')
      : null,
    start,
    cancel,
    syncPhone,
  }
}
