import { useCallback, useEffect, useState } from 'react'
import { orderPushApi } from '../api/orderPush'
import { describeDevice } from '@/hooks/useWebPush'
import { arrayBufferToBase64Url, urlBase64ToUint8Array } from '@/utils/webPushEncoding'
import { detectIosEnvironment, getPushUnavailableReason, type PushUnavailableReason } from '@/utils/pushAvailability'
import { getGoodsErrorMessage } from '../utils/orderError'
import { orderPushStorageKey, orderPushStoredAtKey, pruneOrderPushStorage } from '../utils/goodsPush'

interface Options {
  token: string
  /** `OrderWebPushInfoDto.publicKey` from the order. */
  publicKey: string | null | undefined
}

function supported(): boolean {
  return typeof navigator !== 'undefined' && 'serviceWorker' in navigator && typeof window !== 'undefined' && 'PushManager' in window
}

function readPermission(): NotificationPermission | 'unsupported' {
  return typeof Notification === 'undefined' ? 'unsupported' : Notification.permission
}

function readStored(token: string): string | null {
  try {
    pruneOrderPushStorage(window.localStorage, Date.now())
    return window.localStorage.getItem(orderPushStorageKey(token))
  } catch {
    return null
  }
}

/**
 * US-24-21 — the buyer's browser push for ONE order (no account). The permission prompt appears ONLY inside `enable()`
 * (a click), never on mount. `disable()` removes the SERVER row only and never calls `PushSubscription.unsubscribe()`:
 * one browser has one subscription shared with the staff role (ARCHITECTURE_CYCLE24.md §456.3).
 */
export function useOrderPush({ token, publicKey }: Options) {
  const [permission, setPermission] = useState(readPermission)
  const [endpoint, setEndpoint] = useState<string | null>(() => readStored(token))
  const [subscribedHere, setSubscribedHere] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Is the endpoint remembered for this order still THIS browser's endpoint? (the subscription may have been reset)
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
    // The server already decided «platform off / shop off / order finished» in `OrderWebPushInfoDto`; not asked again here.
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
      const registration = await navigator.serviceWorker.register('/sw.js')
      await registration.update()
      let sub = await registration.pushManager.getSubscription()
      if (!sub) {
        sub = await registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: urlBase64ToUint8Array(publicKey) as BufferSource })
      }
      await orderPushApi.subscribe(token, {
        endpoint: sub.endpoint,
        keys: { p256dh: arrayBufferToBase64Url(sub.getKey('p256dh')), auth: arrayBufferToBase64Url(sub.getKey('auth')) },
        deviceLabel: describeDevice(),
      })
      try {
        window.localStorage.setItem(orderPushStorageKey(token), sub.endpoint)
        window.localStorage.setItem(orderPushStoredAtKey(token), String(Date.now()))
      } catch {
        // storage blocked — the subscription still works, the button just cannot remember it
      }
      setEndpoint(sub.endpoint)
      setSubscribedHere(true)
    } catch (err) {
      setError(err instanceof Error && !('response' in err) ? err.message : getGoodsErrorMessage(err, 'Не удалось включить уведомления.'))
    } finally {
      setBusy(false)
    }
  }, [publicKey, token])

  const disable = useCallback(async () => {
    setError(null)
    setBusy(true)
    try {
      if (endpoint) await orderPushApi.unsubscribe(token, endpoint)
      try {
        window.localStorage.removeItem(orderPushStorageKey(token))
        window.localStorage.removeItem(orderPushStoredAtKey(token))
      } catch {
        // ignore
      }
      setEndpoint(null)
      setSubscribedHere(false)
    } catch (err) {
      setError(getGoodsErrorMessage(err, 'Не удалось отключить уведомления.'))
    } finally {
      setBusy(false)
    }
  }, [endpoint, token])

  return { reason, subscribed: subscribedHere, busy, error, enable, disable }
}
