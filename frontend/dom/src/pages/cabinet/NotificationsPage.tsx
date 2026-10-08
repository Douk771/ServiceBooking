import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { ChannelRequestModal } from '@/components/notifications/ChannelRequestModal'
import { ChannelCard } from '@/components/notifications/LegacyChannelCard'
import { notificationChannelsApi } from '@/api/notificationChannels'
import { TRANSPORT_LABELS } from '@/utils/notificationTransport'
import { formatRub } from '@/utils/money'
import type { Company } from '@/types'
import { staysCompaniesApi } from '../../api/staysCompanies'
import { SwitchRow } from '../../components/cabinet/formParts'
import { ErrorState, InlineError, LoadingList } from '../../components/StatePanels'
import { useStaysCompany } from '../../hooks/useStaysCompany'
import type { StaysNotificationSettingsDto } from '../../types'
import { can } from '../../utils/permissions'
import { getStayErrorMessage, readConflict } from '../../utils/stayError'
import { NotFoundPage } from '../NotFoundPage'

const MODES = [
  { value: 'PriorityChannel', label: 'В приоритетный мессенджер' },
  { value: 'AllChannels', label: 'Во все подключённые' },
]

/**
 * `/cabinet/:companyId/notifications` (`ManageCompany`, US-37-29/30) — what the company sends: push and MAX to staff, the guest's browser
 * push and messages in MAX/WhatsApp. Whether messages are possible (`messengerAvailable`) is the server's; the numbers are connected with
 * the SAME shared components as on ezbook and goods (`api/notification-channels`). The guest always sees everything on the booking page,
 * so a switched-off channel loses nothing.
 */
export function NotificationsPage() {
  const { company } = useStaysCompany()
  const qc = useQueryClient()
  const key = ['stays-notification-settings', company.id]
  const q = useQuery({ queryKey: key, queryFn: () => staysCompaniesApi.notificationSettings(company.id) })
  const [draft, setDraft] = useState<StaysNotificationSettingsDto | null>(null)
  const [saved, setSaved] = useState(false)
  const [showRequest, setShowRequest] = useState(false)
  const channels = useQuery({ queryKey: ['notification-channels'], queryFn: notificationChannelsApi.list })
  const offer = useQuery({ queryKey: ['notification-channel-offer'], queryFn: notificationChannelsApi.offer })

  useEffect(() => {
    if (q.data) setDraft(q.data)
  }, [q.data])

  const save = useMutation({
    mutationFn: (d: StaysNotificationSettingsDto) =>
      staysCompaniesApi.updateNotificationSettings(company.id, {
        staffPushEnabled: d.staffPushEnabled,
        staffMaxEnabled: d.staffMaxEnabled,
        guestWebPushEnabled: d.guestWebPushEnabled,
        guestMessengerEnabled: d.guestMessengerEnabled,
        deliveryMode: d.deliveryMode,
        priorityTransport: d.priorityTransport,
      }),
    onSuccess: (dto) => {
      qc.setQueryData(key, dto)
      setDraft(dto)
      setSaved(true)
    },
    onError: () => void qc.invalidateQueries({ queryKey: key }),
  })

  if (!can(company.myPermissions, 'ManageCompany')) return <NotFoundPage title="Раздел недоступен" hint="Уведомления настраивает владелец." />

  if (q.isLoading || !draft) {
    return (
      <main className="mx-auto max-w-[860px] px-4 pt-8 sm:px-8">
        {q.isError ? <ErrorState message={getStayErrorMessage(q.error, 'Не удалось загрузить настройки уведомлений.')} onRetry={() => void q.refetch()} /> : <LoadingList rows={3} rowClass="h-32" />}
      </main>
    )
  }

  const set = (patch: Partial<StaysNotificationSettingsDto>) => {
    setDraft({ ...draft, ...patch })
    setSaved(false)
    save.reset()
  }
  const differs = JSON.stringify(draft) !== JSON.stringify(q.data)
  const companyAsList = [{ id: company.id, name: company.name }] as unknown as Company[]
  const ownChannels = channels.data ?? []
  const assignedElsewhere = (channelId: string, transport: string) => {
    const ids = new Set<string>()
    for (const ch of ownChannels) if (ch.id !== channelId && ch.transport === transport) ch.companies.forEach((c) => ids.add(c.companyId))
    return ids
  }
  const transports = [...new Set(ownChannels.filter((c) => c.companies.some((x) => x.companyId === company.id)).map((c) => c.transport))]
  const saveConflict = save.isError ? readConflict<{ code: string; message: string }>(save.error) : null

  return (
    <main className="mx-auto flex max-w-[860px] flex-col gap-6 px-4 pb-12 pt-8 sm:px-8">
      <section aria-labelledby="ntf-staff" className="rounded-3xl border border-line bg-white p-5 sm:p-7">
        <h2 id="ntf-staff" className="font-serif text-[22px] text-ink">
          Сотрудникам
        </h2>
        <div className="mt-3 flex flex-col gap-4">
          <SwitchRow
            label="Push о новых бронях и подтверждениях оплаты"
            hint={
              <>
                Сотрудник включает уведомления на своём устройстве в{' '}
                <Link to="/profile#devices" className="underline">
                  профиле
                </Link>
                , раздел «Устройства и уведомления». В push нет имени и телефона гостя.
              </>
            }
            checked={draft.staffPushEnabled}
            onChange={(v) => set({ staffPushEnabled: v })}
          />
          <SwitchRow
            label="Сообщения сотрудникам в MAX"
            hint="Приходят тем, кто подключил MAX в профиле. Горничной сообщения о бронях не приходят."
            checked={draft.staffMaxEnabled}
            onChange={(v) => set({ staffMaxEnabled: v })}
          />
        </div>
      </section>

      <section aria-labelledby="ntf-guests" className="rounded-3xl border border-line bg-white p-5 sm:p-7">
        <h2 id="ntf-guests" className="font-serif text-[22px] text-ink">
          Гостям
        </h2>
        <div className="mt-3 flex flex-col gap-4">
          <SwitchRow
            label="Уведомления в браузере"
            hint="На странице брони гость может включить уведомление о смене статуса. Без имён, адресов и кодов."
            checked={draft.guestWebPushEnabled}
            onChange={(v) => set({ guestWebPushEnabled: v })}
          />
          <SwitchRow
            label="Сообщения в MAX/WhatsApp"
            hint={
              draft.messengerAvailable
                ? 'Получит только тот гость, кто отдельно отметил это при бронировании. Информация к заселению уходит ссылкой, если вы не включили отправку текста целиком.'
                : 'Подключите канал WhatsApp или MAX, чтобы отправлять сообщения гостям'
            }
            checked={draft.guestMessengerEnabled}
            disabled={!draft.messengerAvailable}
            onChange={(v) => set({ guestMessengerEnabled: v })}
          />
        </div>

        {draft.messengerAvailable && draft.guestMessengerEnabled && (
          <div className="mt-4 flex flex-col gap-4">
            <fieldset>
              <legend className="mb-2 text-[13px] font-medium text-[#4A4038]">Куда отправлять</legend>
              <div className="flex flex-wrap gap-2">
                {MODES.map((m) => (
                  <label key={m.value} className={`inline-flex min-h-[44px] cursor-pointer items-center gap-2 rounded-full border px-4 text-sm ${draft.deliveryMode === m.value ? 'border-ink bg-cream-deep/60 font-semibold' : 'border-line'}`}>
                    <input type="radio" name="delivery-mode" className="accent-gold" checked={draft.deliveryMode === m.value} onChange={() => set({ deliveryMode: m.value })} />
                    {m.label}
                  </label>
                ))}
              </div>
            </fieldset>
            {draft.deliveryMode === 'PriorityChannel' && transports.length > 1 && (
              <fieldset>
                <legend className="mb-2 text-[13px] font-medium text-[#4A4038]">Приоритетный мессенджер</legend>
                <div className="flex flex-wrap gap-2">
                  {transports.map((t) => (
                    <label key={t} className={`inline-flex min-h-[44px] cursor-pointer items-center gap-2 rounded-full border px-4 text-sm ${draft.priorityTransport === t ? 'border-ink bg-cream-deep/60 font-semibold' : 'border-line'}`}>
                      <input type="radio" name="priority-transport" className="accent-gold" checked={draft.priorityTransport === t} onChange={() => set({ priorityTransport: t })} />
                      {TRANSPORT_LABELS[t] ?? t}
                    </label>
                  ))}
                </div>
              </fieldset>
            )}
          </div>
        )}

        {save.isError && (
          <div className="mt-4" data-code={saveConflict?.code}>
            <InlineError>{saveConflict?.message ?? getStayErrorMessage(save.error, 'Не удалось сохранить настройки уведомлений.')}</InlineError>
          </div>
        )}
        <div className="mt-5 flex flex-wrap items-center gap-3">
          <Button loading={save.isPending} disabled={!differs} onClick={() => save.mutate(draft)} className="min-h-[44px]">
            Сохранить
          </Button>
          {saved && !differs && (
            <span role="status" className="text-sm font-medium text-success">
              Сохранено
            </span>
          )}
        </div>
      </section>

      <section aria-labelledby="ntf-channels" className="flex flex-col gap-3">
        <h2 id="ntf-channels" className="font-serif text-[22px] text-ink">
          Номера для сообщений
        </h2>
        {channels.isLoading ? (
          <LoadingList rows={1} rowClass="h-28" />
        ) : channels.isError ? (
          <ErrorState message={getStayErrorMessage(channels.error, 'Не удалось загрузить номера.')} onRetry={() => void channels.refetch()} />
        ) : (
          ownChannels.map((ch) => (
            <ChannelCard key={ch.id} channel={ch} myCompanies={companyAsList} assignedElsewhereIds={assignedElsewhere(ch.id, ch.transport)} riskVersion={offer.data?.riskVersion} />
          ))
        )}
        {offer.data && offer.data.pricePerMonth != null && offer.data.allowedByPlan ? (
          <Card className="flex flex-wrap items-center justify-between gap-4 p-5">
            <p className="text-sm text-ink-soft">
              Сообщения уходят гостям с вашего номера. {formatRub(offer.data.pricePerMonth)} / мес за номер; оплату включает администратор по заявке в разделе «Подписка».
            </p>
            <Button onClick={() => setShowRequest(true)} className="min-h-[44px]">
              Подключить номер
            </Button>
          </Card>
        ) : offer.data ? (
          <p className="text-sm text-muted">Подключение номера недоступно на вашем тарифе или временно закрыто.</p>
        ) : null}
        {showRequest && <ChannelRequestModal onClose={() => setShowRequest(false)} />}
      </section>
    </main>
  )
}
