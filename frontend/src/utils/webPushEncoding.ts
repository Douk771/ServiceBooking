// Pure byte-shuffling helpers shared by useWebPush.ts. Split out so the encode/decode round-trip can
// be unit-tested without touching `PushManager`/`ServiceWorkerRegistration`, which jsdom doesn't have.

/** VAPID public key (base64url, from GET /api/push/config) → Uint8Array for `applicationServerKey`. */
export function urlBase64ToUint8Array(base64Url: string): Uint8Array {
  const padding = '='.repeat((4 - (base64Url.length % 4)) % 4)
  const base64 = (base64Url + padding).replace(/-/g, '+').replace(/_/g, '/')
  const raw = atob(base64)
  const output = new Uint8Array(raw.length)
  for (let i = 0; i < raw.length; i++) output[i] = raw.charCodeAt(i)
  return output
}

/** `PushSubscription.getKey('p256dh' | 'auth')` (an ArrayBuffer) → base64url, for the JSON body of
 *  POST /api/push/subscriptions. */
export function arrayBufferToBase64Url(buffer: ArrayBuffer | null): string {
  if (!buffer) return ''
  const bytes = new Uint8Array(buffer)
  let binary = ''
  for (const byte of bytes) binary += String.fromCharCode(byte)
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}
