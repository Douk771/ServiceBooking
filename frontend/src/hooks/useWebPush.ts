import { useCallback, useEffect, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { pushApi, type PushSite } from '../api/push'
import { urlBase64ToUint8Array, arrayBufferToBase64Url } from '../utils/webPushEncoding'
import { detectIosEnvironment, getPushUnavailableReason, type PushUnavailableReason } from '../utils/pushAvailability'
import { refreshPushWorkerPeer, registerPushWorker } from '../utils/pushWorker'
import type { PushConfigCompany, PushSiteUrls, PushSubscriptionDevice } from '../types'

// ARCHITECTURE_CYCLE9.md §105.5/§105.9/§105.10, API_CONTRACT_CYCLE9.md §115 — feature detection,
// SW registration, subscribe/unsubscribe and server reconciliation for the master's own device.
//
// Permission is requested ONLY inside enableOnThisDevice(), never on mount — browsers punish an
// unprompted permission request and there is no second chance to ask (US-118).

const SERVICE_WORKER_SUPPORTED = typeof navigator !== 'undefined' && 'serviceWorker' in navigator && 'PushManager' in window
const EMPTY_DEVICES: PushSubscriptionDevice[] = []
const EMPTY_COMPANIES: PushConfigCompany[] = []

/** ARCHITECTURE_CYCLE21.md §362 — iOS + "opened from the Home Screen" detection, safe outside a browser. */
function readIosEnvironment() {
  if (typeof navigator === 'undefined') return { isIos: false, isStandalone: false, version: null }
  const displayModeStandalone =
    typeof window !== 'undefined' && typeof window.matchMedia === 'function'
      ? window.matchMedia('(display-mode: standalone)').matches
      : false
  return detectIosEnvironment(navigator, displayModeStandalone)
}

function readPermission(): NotificationPermission | 'unsupported' {
  if (typeof Notification === 'undefined') return 'unsupported'
  return Notification.permission
}

/**
 * §105.5 rubezh 2 (Q16) — "the browser on a shared computer keeps sending notifications with client
 * names to whoever logged in first" is closed by this call at logout, together with rubezh 1 (the
 * server reassigns an endpoint to whoever re-subscribes with it) and rubezh 3 (server-side membership
 * check at send time). Best-effort and silent on purpose: it runs from a plain function, not a React
 * hook, so it can be called both from the explicit "Log out" button (Navbar.tsx) and from the 401
 * interceptor (api/client.ts); a missing SW registration or a network failure must never block logout
 * itself — rubezh 1 will pick up the slack the next time someone subscribes on this browser.
 */
export async function unsubscribeCurrentDeviceOnLogout(opts: { keepBrowserSubscription?: boolean } = {}): Promise<void> {
  if (!SERVICE_WORKER_SUPPORTED) return
  try {
    const registration = await navigator.serviceWorker.getRegistration('/')
    const subscription = await registration?.pushManager.getSubscription()
    if (!subscription) return
    await pushApi.deleteCurrent(subscription.endpoint)
    // goods: the browser subscription is shared with the buyer role (ARCHITECTURE_CYCLE33.md §33.9.2) — server row only.
    if (!opts.keepBrowserSubscription) await subscription.unsubscribe()
  } catch {
    // Best-effort (§105.5) — no network, no registration, or the server call failed: logout proceeds
    // regardless.
  }
}

interface UseWebPushResult {
  /** Single reason to show in PushUnavailableNotice, or null when the toggle should be offered. */
  reason: PushUnavailableReason | null
  /** Whether the request for config/devices is still in flight. */
  isLoading: boolean
  /** True once this exact browser+device is subscribed (its endpoint is on the server's list). */
  isSubscribedOnThisDevice: boolean
  /** All of the user's devices of BOTH sites, including this one (ARCHITECTURE_CYCLE33.md §33.9.2). */
  devices: PushSubscriptionDevice[]
  /** Companies where the user is Master/CompanyOwner, both kinds; empty = not staff. */
  companies: PushConfigCompany[]
  hasServices: boolean
  hasOrders: boolean
  /** `undefined` while the config is loading (role not known yet). */
  isStaff: boolean | undefined
  siteUrls: PushSiteUrls | undefined
  /** The device list request failed (the switch may still work). */
  devicesError: boolean
  isEnabling: boolean
  isDisabling: boolean
  actionError: string | null
  /** Explicit user action: request permission, register SW, subscribe, POST to server. */
  enableOnThisDevice: () => Promise<void>
  /** Unsubscribe this device specifically. */
  disableOnThisDevice: () => Promise<void>
  /** Disable ANY of the master's devices, including ones that aren't this one (§115.4). */
  disableDevice: (id: string) => Promise<void>
}

/**
 * ARCHITECTURE_CYCLE33.md §33.9.2. `site` is where the page lives (sent with every POST); config and devices are always
 * requested for BOTH sites (`allSites=true`).
 * - `keepBrowserSubscription: true` — goods: one browser has ONE push subscription shared by the staff member and
 *   the customer, so "disable" removes the server row only and never calls PushSubscription.unsubscribe().
 */
export interface UseWebPushOptions {
  site: PushSite
  keepBrowserSubscription?: boolean
}

export function useWebPush(options: UseWebPushOptions): UseWebPushResult {
  const { site, keepBrowserSubscription = false } = options
  const qc = useQueryClient()
  const [permission, setPermission] = useState<NotificationPermission | 'unsupported'>(readPermission)
  const [currentEndpoint, setCurrentEndpoint] = useState<string | null>(null)
  const [isEnabling, setIsEnabling] = useState(false)
  const [isDisabling, setIsDisabling] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)

  // Not gated by SERVICE_WORKER_SUPPORTED: iPhone Safari must still learn the role to show the «install the app» steps.
  const configQuery = useQuery({
    queryKey: ['push-config', site, 'all'],
    queryFn: () => pushApi.getConfig(site, { allSites: true }),
    staleTime: 60 * 1000,
  })

  // Reconciliation (§105.9): find this browser's own subscription, if any, so we know its endpoint
  // before asking the server which devices are current — matches useWebPush entirely off `if`s, no
  // permission prompt involved.
  useEffect(() => {
    if (!SERVICE_WORKER_SUPPORTED) return
    let cancelled = false
    ;(async () => {
      try {
        const registration = await navigator.serviceWorker.getRegistration('/')
        const sub = await registration?.pushManager.getSubscription()
        if (!cancelled) setCurrentEndpoint(sub?.endpoint ?? null)
      } catch {
        // No registration yet — normal before the master has ever enabled push on this device.
      }
    })()
    return () => {
      cancelled = true
    }
  }, [])

  const companies = configQuery.data?.companies ?? EMPTY_COMPANIES
  const hasServices = companies.some((c) => c.kind === 'Services')
  const hasOrders = companies.some((c) => c.kind === 'Orders')
  const isStaff = configQuery.data ? companies.length > 0 : undefined
  const siteUrls = configQuery.data?.siteUrls
  const peerOrigin = siteUrls ? (site === 'Services' ? siteUrls.orders : siteUrls.services) : undefined

  // Devices are listed from any browser (also where push is unsupported): a forgotten device can be removed from anywhere.
  const devicesQuery = useQuery({
    queryKey: ['push-devices', site, 'all', currentEndpoint],
    queryFn: () => pushApi.listSubscriptions(currentEndpoint ?? undefined, site, { allSites: true }),
    enabled: configQuery.data?.enabled === true && companies.length > 0,
  })

  const devices = devicesQuery.data ?? EMPTY_DEVICES
  const isSubscribedOnThisDevice = devices.some((d) => d.isCurrent)

  // §33.5.3: devices enabled before cycle 33 learn the sibling site on the first open. Silent; never installs a worker.
  useEffect(() => {
    if (!SERVICE_WORKER_SUPPORTED || !peerOrigin || companies.length === 0) return
    refreshPushWorkerPeer(peerOrigin).catch(() => {
      // Best-effort: the notification still arrives, only the cross-site click degrades to the default page.
    })
  }, [peerOrigin, companies.length])

  const companyStaffPushEnabled = companies.length > 0 ? companies.some((c) => c.staffPushEnabled) : undefined

  const reason = getPushUnavailableReason({
    serviceWorkerSupported: SERVICE_WORKER_SUPPORTED,
    isSecureContext: typeof window !== 'undefined' && window.isSecureContext,
    permission,
    ios: readIosEnvironment(),
    platformEnabled: configQuery.data?.enabled,
    companyStaffPushEnabled,
  })

  const invalidateDevices = useCallback(() => {
    qc.invalidateQueries({ queryKey: ['push-devices'] })
  }, [qc])

  const enableOnThisDevice = useCallback(async () => {
    setActionError(null)
    setIsEnabling(true)
    try {
      // Explicit user action → the ONE place this is ever called (US-118).
      const perm = await Notification.requestPermission()
      setPermission(perm)
      if (perm !== 'granted') return

      const registration = await registerPushWorker(peerOrigin)
      await registration.update()

      const publicKey = configQuery.data?.publicKey
      if (!publicKey) throw new Error('Push отключён на платформе')

      let subscription = await registration.pushManager.getSubscription()
      if (!subscription) {
        subscription = await registration.pushManager.subscribe({
          userVisibleOnly: true,
          applicationServerKey: urlBase64ToUint8Array(publicKey) as BufferSource,
        })
      }

      const created = await pushApi.subscribe({
        endpoint: subscription.endpoint,
        keys: {
          p256dh: arrayBufferToBase64Url(subscription.getKey('p256dh')),
          auth: arrayBufferToBase64Url(subscription.getKey('auth')),
        },
        deviceLabel: describeDevice(),
        site,
      })
      setCurrentEndpoint(subscription.endpoint)
      qc.setQueryData(['push-devices', site, 'all', subscription.endpoint], (prev: PushSubscriptionDevice[] | undefined) => {
        const rest = (prev ?? []).filter((d) => d.id !== created.id)
        return [...rest, { ...created, isCurrent: true }]
      })
      invalidateDevices()
    } catch (err) {
      setActionError(err instanceof Error ? err.message : 'Не удалось включить уведомления')
    } finally {
      setIsEnabling(false)
    }
  }, [configQuery.data?.publicKey, invalidateDevices, peerOrigin, qc, site])

  const disableOnThisDevice = useCallback(async () => {
    setActionError(null)
    setIsDisabling(true)
    try {
      const registration = await navigator.serviceWorker.getRegistration('/')
      const subscription = await registration?.pushManager.getSubscription()
      const current = devices.find((d) => d.isCurrent)
      if (current) await pushApi.deleteSubscription(current.id)
      if (!keepBrowserSubscription) await subscription?.unsubscribe()
      if (!keepBrowserSubscription) setCurrentEndpoint(null)
      invalidateDevices()
    } catch (err) {
      setActionError(err instanceof Error ? err.message : 'Не удалось отключить уведомления')
    } finally {
      setIsDisabling(false)
    }
  }, [devices, invalidateDevices, keepBrowserSubscription])

  const disableDevice = useCallback(
    async (id: string) => {
      setActionError(null)
      setIsDisabling(true)
      try {
        await pushApi.deleteSubscription(id)
        const device = devices.find((d) => d.id === id)
        if (device?.isCurrent && !keepBrowserSubscription) {
          const registration = await navigator.serviceWorker.getRegistration('/')
          const subscription = await registration?.pushManager.getSubscription()
          await subscription?.unsubscribe()
          setCurrentEndpoint(null)
        }
        invalidateDevices()
      } catch (err) {
        setActionError(err instanceof Error ? err.message : 'Не удалось отключить устройство')
      } finally {
        setIsDisabling(false)
      }
    },
    [devices, invalidateDevices, keepBrowserSubscription],
  )

  return {
    reason,
    isLoading: configQuery.isLoading || devicesQuery.isLoading,
    companies,
    hasServices,
    hasOrders,
    isStaff,
    siteUrls,
    devicesError: devicesQuery.isError,
    isSubscribedOnThisDevice,
    devices,
    isEnabling,
    isDisabling,
    actionError,
    enableOnThisDevice,
    disableOnThisDevice,
    disableDevice,
  }
}

export function describeDevice(): string {
  // Best-effort, human label only ("Chrome на Windows") — the endpoint, not this string, is what
  // identifies the device to the server.
  const ua = navigator.userAgent
  const browser = /Edg\//.test(ua) ? 'Edge' : /Chrome\//.test(ua) ? 'Chrome' : /Firefox\//.test(ua) ? 'Firefox' : /Safari\//.test(ua) ? 'Safari' : 'Браузер'
  const os = /Windows/.test(ua) ? 'Windows' : /Mac OS X/.test(ua) ? 'macOS' : /Android/.test(ua) ? 'Android' : /iPhone|iPad/.test(ua) ? 'iOS' : /Linux/.test(ua) ? 'Linux' : ''
  return os ? `${browser} на ${os}` : browser
}
