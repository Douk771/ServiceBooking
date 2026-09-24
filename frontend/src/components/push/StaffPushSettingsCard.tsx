import { useEffect, useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { pushApi } from '../../api/push'
import { getPushErrorMessage } from '../../utils/pushError'
import { Card } from '../ui/Card'
import { Icon } from '../ui/Icon'

/**
 * ARCHITECTURE_CYCLE9.md §105.7/§105.4, API_CONTRACT_CYCLE9.md §115.5 (US-117, C18) — "notify staff
 * about new bookings", default ON. Deliberately its own card, own query, own route
 * (`staff-push-settings`): unlike everything else on this tab it is NOT gated by
 * `planAllowsChannel`/channel state — §105.4 explains why (`notification-settings` 402s on every
 * tariff today, and a free feature can't live behind that gate).
 */
export function StaffPushSettingsCard({ companyId }: { companyId: string }) {
  const qc = useQueryClient()
  const { data, isLoading, isError } = useQuery({
    queryKey: ['staff-push-settings', companyId],
    queryFn: () => pushApi.getCompanySettings(companyId),
  })

  const [enabled, setEnabled] = useState(true)
  useEffect(() => {
    if (data) setEnabled(data.staffPushEnabled)
  }, [data])

  const saveMut = useMutation({
    mutationFn: (next: boolean) => pushApi.updateCompanySettings(companyId, next),
    onSuccess: (res) => qc.setQueryData(['staff-push-settings', companyId], res),
  })

  const onToggle = () => {
    const next = !enabled
    setEnabled(next)
    saveMut.mutate(next)
  }

  if (isLoading) return <div className="h-20 bg-cream-deep rounded-2xl animate-pulse" />
  if (isError || !data)
    return (
      <Card className="p-6 text-center text-muted">
        <Icon name="alert-circle" size={20} strokeWidth={1.6} className="mx-auto mb-1.5" />
        <p className="text-sm">Не удалось загрузить настройку уведомлений сотрудникам.</p>
      </Card>
    )

  return (
    <Card className="p-6">
      <label className="flex items-center justify-between gap-3">
        <span>
          <span className="block text-sm font-medium text-ink">Уведомлять сотрудников о новых записях</span>
          <span className="block text-xs text-muted mt-0.5">
            Push в браузер мастера, когда клиент записался. Бесплатно, не зависит от тарифа.
          </span>
        </span>
        <button
          type="button"
          role="switch"
          aria-checked={enabled}
          disabled={saveMut.isPending}
          onClick={onToggle}
          className={`relative w-11 h-6 rounded-full transition-colors shrink-0 disabled:opacity-50 ${enabled ? 'bg-ink' : 'bg-line-strong'}`}
        >
          <span className={`absolute top-0.5 left-0.5 w-5 h-5 rounded-full bg-white transition-transform ${enabled ? 'translate-x-5' : ''}`} />
        </button>
      </label>
      {saveMut.isError && <p className="text-sm text-danger mt-2">{getPushErrorMessage(saveMut.error)}</p>}
    </Card>
  )
}
