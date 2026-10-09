import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { notificationsApi } from '../api/notifications'
import { optInPayloadValue, resolveOptInDefault, type OptInDefault } from '../utils/messengerOptIn'

export interface MessengerOptInState {
  /** Умолчание по §40.11.4 (`guest` | `optedOut` | `prechecked` | `unchecked`). */
  initial: OptInDefault
  checked: boolean
  setChecked: (v: boolean) => void
  /** Вошедший отписан в профиле: вместо галочки строка со ссылкой, поле не отправляется. */
  optedOut: boolean
  loading: boolean
  /** Значение для `notifyByMessenger` в запросе: `undefined` — не отправлять (не предложено или отписан). */
  payload: (offered: boolean) => boolean | undefined
}

/**
 * Предзаполнение галочки (ARCHITECTURE_CYCLE40.md §40.11.4, Т40-L-09): гость — снята; вошедший — стоит заранее только при
 * `enabled ∧ providerDeliveryConsent`. Выбор человека после первого клика не перетирается загрузкой профиля.
 * Профиль читается только у вошедшего; ошибка запроса = «снята» (безопасное умолчание).
 */
export function useMessengerOptInDefault(authenticated: boolean): MessengerOptInState {
  const prefs = useQuery({
    queryKey: ['notification-preferences'],
    queryFn: notificationsApi.getPreferences,
    enabled: authenticated,
    staleTime: 60 * 1000,
    retry: false,
  })
  const [touched, setTouched] = useState<boolean | null>(null)

  const initial = resolveOptInDefault(authenticated, prefs.data)
  const optedOut = initial.kind === 'optedOut'
  const checked = optedOut ? false : (touched ?? initial.checked)

  return {
    initial,
    checked,
    setChecked: setTouched,
    optedOut,
    loading: authenticated && prefs.isLoading,
    payload: (offered) => optInPayloadValue(offered, optedOut, checked),
  }
}
