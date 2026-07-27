import { useEffect, useRef } from 'react'

/**
 * Yandex SmartCaptcha widget for guest booking.
 *
 * The client site key comes from VITE_SMARTCAPTCHA_SITEKEY. When it's absent (local dev) the widget
 * renders nothing and never yields a token — which is fine, because the backend skips captcha outside
 * Production. In Production the key must be set (the backend fails closed without server-side validation).
 *
 * Loads Yandex's script once, renders the widget, and reports the one-time token via onToken. The token
 * is single-use and expires, so we clear it (onToken('')) when the widget signals expiry/error.
 */
declare global {
  interface Window {
    smartCaptcha?: {
      render: (
        container: HTMLElement,
        params: { sitekey: string; callback?: (token: string) => void; hl?: string },
      ) => number
      destroy?: (widgetId: number) => void
    }
  }
}

const SCRIPT_SRC = 'https://smartcaptcha.yandexcloud.net/captcha.js'
const siteKey = import.meta.env.VITE_SMARTCAPTCHA_SITEKEY as string | undefined

/** True when SmartCaptcha is configured on the client — callers use it to gate submit on a token. */
export const smartCaptchaEnabled = !!siteKey

function loadScript(): Promise<void> {
  if (window.smartCaptcha) return Promise.resolve()
  return new Promise((resolve, reject) => {
    const existing = document.querySelector<HTMLScriptElement>(`script[src="${SCRIPT_SRC}"]`)
    if (existing) {
      existing.addEventListener('load', () => resolve())
      existing.addEventListener('error', () => reject(new Error('SmartCaptcha script failed to load')))
      return
    }
    const script = document.createElement('script')
    script.src = SCRIPT_SRC
    script.async = true
    script.onload = () => resolve()
    script.onerror = () => reject(new Error('SmartCaptcha script failed to load'))
    document.head.appendChild(script)
  })
}

export function SmartCaptcha({ onToken }: { onToken: (token: string) => void }) {
  const containerRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!siteKey || !containerRef.current) return
    let widgetId: number | undefined
    let cancelled = false

    loadScript()
      .then(() => {
        if (cancelled || !window.smartCaptcha || !containerRef.current) return
        widgetId = window.smartCaptcha.render(containerRef.current, {
          sitekey: siteKey,
          hl: 'ru',
          callback: (token: string) => onToken(token),
        })
      })
      .catch(() => onToken(''))

    return () => {
      cancelled = true
      if (widgetId !== undefined) window.smartCaptcha?.destroy?.(widgetId)
    }
  }, [onToken])

  if (!siteKey) return null
  return <div ref={containerRef} className="mt-1" />
}
