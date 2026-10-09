import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { NumbersBlock } from '@/components/notifications/NumbersBlock'
import { notificationNumbersApi, NUMBERS_OVERVIEW_QUERY_KEY } from '@/api/notificationNumbers'
import { TRANSPORT_LABELS } from '@/utils/notificationTransport'
import { SwitchRow } from '@/components/slots/ui/formParts'
import { ErrorState, InlineError, LoadingList } from '@/components/slots/ui/StatePanels'
import { can } from '@/utils/slots/slotPermissions'
import { getStayErrorMessage, readConflict } from '@/utils/slots/slotError'
import { bathsCabinetApi } from '../../api/bathsCabinet'
import { useBathsCompany } from '../../cabinet/cabinetVertical'
import type { NotificationSettingsDto } from '../../cabinet/types'
import { NotFoundPage } from '../NotFoundPage'

const MODES = [
  { value: 'PriorityChannel', label: 'В приоритетный мессенджер' },
  { value: 'AllChannels', label: 'Во все подключённые' },
]

/**
 * `/cabinet/:companyId/notifications` (`ManageCompany`) — what the company sends: push and MAX to staff, the guest's browser
 * push and messages in MAX/WhatsApp. Whether messages are possible (`messengerAvailable`) is the server's; the numbers are the account's, in the
 * SAME «Номера» block as on ezbook and goods (cycle 40: payment request → terms → QR; no assignment of companies). The guest always sees everything on the booking page,
 * so a switched-off channel loses nothing.
 */
export function NotificationsPage() {
  const { company } = useBathsCompany()
  const qc = useQueryClient()
  const key = ['baths-notification-settings', company.id]
  const q = useQuery({ queryKey: key, queryFn: () => bathsCabinetApi.notificationSettings(company.id) })
  const [draft, setDraft] = useState<NotificationSettingsDto | null>(null)
  const [saved, setSaved] = useState(false)
  // The working messengers come from the shared overview (the SAME query the numbers block uses, so one request serves both).
  const overview = useQuery({ queryKey: NUMBERS_OVERVIEW_QUERY_KEY, queryFn: notificationNumbersApi.overview })

  useEffect(() => {
    if (q.data) setDraft(q.data)
  }, [q.data])

  const save = useMutation({
    mutationFn: (d: NotificationSettingsDto) =>
      bathsCabinetApi.updateNotificationSettings(company.id, {
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

  const set = (patch: Partial<NotificationSettingsDto>) => {
    setDraft({ ...draft, ...patch })
    setSaved(false)
    save.reset()
  }
  const differs = JSON.stringify(draft) !== JSON.stringify(q.data)
  // Working = a bound number of a paid transport; a saved priority that stopped working stays visible and marked.
  const working = (overview.data?.transports ?? []).filter((t) => t.channel?.displayStatus === 'Working').map((t) => t.transport)
  const saved0 = draft.priorityTransport as (typeof working)[number] | null | undefined
  const priorityOptions = [...working, ...(saved0 && !working.includes(saved0) ? [saved0] : [])]
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
            hint="Приходят тем, кто подключил MAX в профиле. Банщику сообщения о бронях не приходят."
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
                ? 'Получит только тот гость, кто отдельно отметил это при бронировании. В сообщении — ссылка на страницу брони.'
                : 'Подключите канал WhatsApp или MAX, чтобы отправлять сообщения гостям'
            }
            checked={draft.guestMessengerEnabled}
            disabled={!draft.messengerAvailable}
            onChange={(v) => set({ guestMessengerEnabled: v })}
          />
        </div>

        {draft.messengerAvailable && draft.guestMessengerEnabled && draft.deliveryChoiceVisible && (
          <div className="mt-4 flex flex-col gap-4">
            {draft.priorityWarning && (
              <p className="rounded-xl bg-warning-bg px-4 py-2.5 text-sm text-warning" data-testid="priority-warning">{draft.priorityWarning}</p>
            )}
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
            {draft.deliveryMode === 'PriorityChannel' && (
              <fieldset>
                <legend className="mb-2 text-[13px] font-medium text-[#4A4038]">Приоритетный мессенджер</legend>
                <div className="flex flex-wrap gap-2">
                  {priorityOptions.map((t) => (
                    <label key={t} className={`inline-flex min-h-[44px] cursor-pointer items-center gap-2 rounded-full border px-4 text-sm ${draft.priorityTransport === t ? 'border-ink bg-cream-deep/60 font-semibold' : 'border-line'}`}>
                      <input type="radio" name="priority-transport" className="accent-gold" checked={draft.priorityTransport === t} onChange={() => set({ priorityTransport: t })} />
                      {(TRANSPORT_LABELS[t] ?? t) + (working.includes(t) ? '' : ' (не работает)')}
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

      <NumbersBlock />
    </main>
  )
}
