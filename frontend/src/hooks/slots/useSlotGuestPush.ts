import { useCallback, useEffect, useState } from 'react'
import { describeDevice } from '@/hooks/useWebPush'
import { registerPushWorker } from '@/utils/pushWorker'
import { arrayBufferToBase64Url, urlBase64ToUint8Array } from '@/utils/webPushEncoding'
import { detectIosEnvironment, getPushUnavailableReason, type PushUnavailableReason } from '@/utils/pushAvailability'
import type { PushSubscriptionInput } from '@/types/slots'
import { getStayErrorMessage } from '@/utils/slots/slotError'
import { bookingPushAtKey, bookingPushKey, pruneBookingPushStorage } from '@/utils/slots/slotPush'

/** The two push routes of a guest page (a booking, a separate session): the endpoints differ, the mechanism is the same. */
export interface GuestPushApi {
  pushSubscribe: (token: string, input: PushSubscriptionInput) => Promise<void>
  pushUnsubscribe: (token: string, endpoint: string) => Promise<void>
}

export interface SlotGuestPushOptions {
  /** A booking (`/b/<token>`) or a separate session (`/s/<token>`); the endpoints differ, the mechanism is the same. */
  kind?: 'booking' | 'order'
  token: string
  /** `BookingNotificationsDto.webPush.publicKey`. */
  publicKey: string | null | undefined
  /** Routes of the page `kind` points at (the vertical picks them). */
  api: GuestPushApi
}

function supported(): boolean {
  return typeof navigator !== 'undefined' && 'serviceWorker' in navigator && typeof window !== 'undefined' && 'PushManager' in window
}

function readPermission(): NotificationPermission | 'unsupported' {
  return typeof Notification === 'undefined' ? 'unsupported' : Notification.permission
}

function readStored(token: string): string | null {
  try {
    pruneBookingPushStorage(window.localStorage, Date.now())
    return window.localStorage.getItem(bookingPushKey(token))
  } catch {
    return null
  }
}

/**
 * The guest's browser push for ONE booking (no account, API_CONTRACT_CYCLE37.md §37.26.5). The permission prompt appears ONLY inside
 * `enable()` (a click), never on mount. `disable()` removes the SERVER row only and never calls `PushSubscription.unsubscribe()`:
 * one browser has one subscription shared with the staff role (ARCHITECTURE_CYCLE37.md §37.14.6).
 */
export function useSlotGuestPush({ kind = 'booking', token: rawToken, publicKey, api }: SlotGuestPushOptions) {
  // one browser may follow a booking and a session with different tokens; the memory keys never collide
  const token = kind === 'order' ? `so:${rawToken}` : rawToken
  const [permission, setPermission] = useState(readPermission)
  const [endpoint, setEndpoint] = useState<string | null>(() => readStored(token))
  const [subscribedHere, setSubscribedHere] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Is the endpoint remembered for this booking still THIS browser's endpoint? (the subscription may have been reset)
  useEffect(() => {
    let cancelled = false
    const stored = readStored(token)
    setEndpoint(stored)
    if (!stored || !supported()) {
      setSubscribedHere(false)
      return
    }
    void (async () => {
      try {
        const reg = await navigator.serviceWorker.getRegistration('/')
        const sub = await reg?.pushManager.getSubscription()
        if (!cancelled) setSubscribedHere(!!sub && sub.endpoint === stored)
      } catch {
        if (!cancelled) setSubscribedHere(false)
      }
    })()
    return () => {
      cancelled = true
    }
  }, [token])

  const reason: PushUnavailableReason | null = getPushUnavailableReason({
    serviceWorkerSupported: supported(),
    isSecureContext: typeof window !== 'undefined' && window.isSecureContext,
    permission,
    ios:
      typeof navigator === 'undefined'
        ? { isIos: false, isStandalone: false, version: null }
        : detectIosEnvironment(navigator, typeof window.matchMedia === 'function' ? window.matchMedia('(display-mode: standalone)').matches : false),
    // «Platform off / company off / booking finished» is the server's call, in `BookingNotificationsDto.webPush`.
    platformEnabled: undefined,
    companyStaffPushEnabled: undefined,
  })

  const enable = useCallback(async () => {
    setError(null)
    setBusy(true)
    try {
      const perm = await Notification.requestPermission() // the ONLY place the browser prompt is triggered
      setPermission(perm)
      if (perm !== 'granted') return
      if (!publicKey) throw new Error('Уведомления в браузере пока не включены на платформе.')
      const registration = await registerPushWorker()
      await registration.update()
      let sub = await registration.pushManager.getSubscription()
      if (!sub) {
        sub = await registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: urlBase64ToUint8Array(publicKey) as BufferSource })
      }
      await api.pushSubscribe(rawToken, {
        endpoint: sub.endpoint,
        keys: { p256dh: arrayBufferToBase64Url(sub.getKey('p256dh')), auth: arrayBufferToBase64Url(sub.getKey('auth')) },
        deviceLabel: describeDevice(),
      })
      try {
        window.localStorage.setItem(bookingPushKey(token), sub.endpoint)
        window.localStorage.setItem(bookingPushAtKey(token), String(Date.now()))
      } catch {
        // storage blocked — the subscription still works, the button just cannot remember it
      }
      setEndpoint(sub.endpoint)
      setSubscribedHere(true)
    } catch (err) {
      setError(err instanceof Error && !('response' in err) ? err.message : getStayErrorMessage(err, 'Не удалось включить уведомления.'))
    } finally {
      setBusy(false)
    }
  }, [publicKey, token, rawToken, api])

  const disable = useCallback(async () => {
    setError(null)
    setBusy(true)
    try {
      if (endpoint) await api.pushUnsubscribe(rawToken, endpoint)
      try {
        window.localStorage.removeItem(bookingPushKey(token))
        window.localStorage.removeItem(bookingPushAtKey(token))
      } catch {
        // ignore
      }
      setEndpoint(null)
      setSubscribedHere(false)
    } catch (err) {
      setError(getStayErrorMessage(err, 'Не удалось отключить уведомления.'))
    } finally {
      setBusy(false)
    }
  }, [endpoint, token, rawToken, api])

  return { reason, subscribed: subscribedHere, busy, error, enable, disable }
}
