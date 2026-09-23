import { useCallback, useEffect, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { pushApi } from '../api/push'
import { urlBase64ToUint8Array, arrayBufferToBase64Url } from '../utils/webPushEncoding'
import { detectIosSafariNotInstalled, getPushUnavailableReason, type PushUnavailableReason } from '../utils/pushAvailability'
import type { PushSubscriptionDevice } from '../types'

// ARCHITECTURE_CYCLE9.md §105.5/§105.9/§105.10, API_CONTRACT_CYCLE9.md §115 — feature detection,
// SW registration, subscribe/unsubscribe and server reconciliation for the master's own device.
//
// Permission is requested ONLY inside enableOnThisDevice(), never on mount — browsers punish an
// unprompted permission request and there is no second chance to ask (US-118).

const SERVICE_WORKER_SUPPORTED = typeof navigator !== 'undefined' && 'serviceWorker' in navigator && 'PushManager' in window
const EMPTY_DEVICES: PushSubscriptionDevice[] = []

function readPermission(): NotificationPermission | 'unsupported' {
  if (typeof Notification === 'undefined') return 'unsupported'
  return Notification.permission
}

export interface UseWebPushResult {
  /** Single reason to show in PushUnavailableNotice, or null when the toggle should be offered. */
  reason: PushUnavailableReason | null
  /** Whether the request for config/devices is still in flight. */
  isLoading: boolean
  /** True once this exact browser+device is subscribed (its endpoint is on the server's list). */
  isSubscribedOnThisDevice: boolean
  /** All of the master's devices, including this one — for MyDevicesCard's list. */
  devices: PushSubscriptionDevice[]
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

export function useWebPush(): UseWebPushResult {
  const qc = useQueryClient()
  const [permission, setPermission] = useState<NotificationPermission | 'unsupported'>(readPermission)
  const [currentEndpoint, setCurrentEndpoint] = useState<string | null>(null)
  const [isEnabling, setIsEnabling] = useState(false)
  const [isDisabling, setIsDisabling] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)

  const configQuery = useQuery({
    queryKey: ['push-config'],
    queryFn: pushApi.getConfig,
    enabled: SERVICE_WORKER_SUPPORTED,
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

  const devicesQuery = useQuery({
    queryKey: ['push-devices', currentEndpoint],
    queryFn: () => pushApi.listSubscriptions(currentEndpoint ?? undefined),
    enabled: SERVICE_WORKER_SUPPORTED && configQuery.data?.enabled === true,
  })

  const devices = devicesQuery.data ?? EMPTY_DEVICES
  const isSubscribedOnThisDevice = devices.some((d) => d.isCurrent)

  const companies = configQuery.data?.companies ?? []
  const companyStaffPushEnabled = companies.length > 0 ? companies.some((c) => c.staffPushEnabled) : undefined

  const reason = getPushUnavailableReason({
    serviceWorkerSupported: SERVICE_WORKER_SUPPORTED,
    isSecureContext: typeof window !== 'undefined' && window.isSecureContext,
    permission,
    isIosSafariNotInstalled: typeof navigator !== 'undefined' ? detectIosSafariNotInstalled(navigator) : false,
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

      const registration = await navigator.serviceWorker.register('/sw.js')
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
      })
      setCurrentEndpoint(subscription.endpoint)
      qc.setQueryData(['push-devices', subscription.endpoint], (prev: PushSubscriptionDevice[] | undefined) => {
        const rest = (prev ?? []).filter((d) => d.id !== created.id)
        return [...rest, { ...created, isCurrent: true }]
      })
      invalidateDevices()
    } catch (err) {
      setActionError(err instanceof Error ? err.message : 'Не удалось включить уведомления')
    } finally {
      setIsEnabling(false)
    }
  }, [configQuery.data?.publicKey, invalidateDevices, qc])

  const disableOnThisDevice = useCallback(async () => {
    setActionError(null)
    setIsDisabling(true)
    try {
      const registration = await navigator.serviceWorker.getRegistration('/')
      const subscription = await registration?.pushManager.getSubscription()
      const current = devices.find((d) => d.isCurrent)
      if (current) await pushApi.deleteSubscription(current.id)
      await subscription?.unsubscribe()
      setCurrentEndpoint(null)
      invalidateDevices()
    } catch (err) {
      setActionError(err instanceof Error ? err.message : 'Не удалось отключить уведомления')
    } finally {
      setIsDisabling(false)
    }
  }, [devices, invalidateDevices])

  const disableDevice = useCallback(
    async (id: string) => {
      setActionError(null)
      setIsDisabling(true)
      try {
        await pushApi.deleteSubscription(id)
        const device = devices.find((d) => d.id === id)
        if (device?.isCurrent) {
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
    [devices, invalidateDevices],
  )

  return {
    reason,
    isLoading: configQuery.isLoading || (configQuery.data?.enabled === true && devicesQuery.isLoading),
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

function describeDevice(): string {
  // Best-effort, human label only ("Chrome на Windows") — the endpoint, not this string, is what
  // identifies the device to the server.
  const ua = navigator.userAgent
  const browser = /Edg\//.test(ua) ? 'Edge' : /Chrome\//.test(ua) ? 'Chrome' : /Firefox\//.test(ua) ? 'Firefox' : /Safari\//.test(ua) ? 'Safari' : 'Браузер'
  const os = /Windows/.test(ua) ? 'Windows' : /Mac OS X/.test(ua) ? 'macOS' : /Android/.test(ua) ? 'Android' : /iPhone|iPad/.test(ua) ? 'iOS' : /Linux/.test(ua) ? 'Linux' : ''
  return os ? `${browser} на ${os}` : browser
}
