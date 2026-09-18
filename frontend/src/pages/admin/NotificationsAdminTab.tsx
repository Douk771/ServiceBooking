import { useEffect, useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import { adminNotificationsApi } from '../../api/platformSettings'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { Input } from '../../components/ui/Input'
import { Modal } from '../../components/ui/Modal'
import { Icon } from '../../components/ui/Icon'
import { Pagination } from '../../components/ui/Pagination'
import { getNotificationErrorMessage } from '../../utils/notificationError'
import type { AdminChannelDto } from '../../types'

function fmt(d: string | null) {
  return d ? format(parseISO(d), 'd MMM yyyy', { locale: ru }) : '—'
}

// ── Summary tile row ─────────────────────────────────────────────────────────

function SummaryRow() {
  const { data } = useQuery({ queryKey: ['admin-channel-summary'], queryFn: adminNotificationsApi.summary })
  if (!data) return <div className="h-20 bg-cream-deep rounded-2xl animate-pulse mb-4" />

  const tiles = [
    { label: 'Подключено', value: data.connected },
    { label: 'Подключается', value: data.connecting },
    { label: 'Отвалилось', value: data.disconnected },
    { label: 'Заблокировано', value: data.blocked },
    { label: 'Требует переподключения', value: data.needsReconnect },
    { label: 'Простаивает', value: data.idle },
    { label: 'Истекает за 7 дней', value: data.expiringIn7Days },
    { label: 'Заявок', value: data.pendingRequests },
  ]

  return (
    <div className="grid grid-cols-2 md:grid-cols-4 gap-3 mb-5">
      {tiles.map((t) => (
        <Card key={t.label} className="p-4 text-center">
          <p className="text-xl font-bold text-ink">{t.value}</p>
          <p className="text-[11px] text-muted mt-0.5">{t.label}</p>
        </Card>
      ))}
    </div>
  )
}

// ── Payment modal ─────────────────────────────────────────────────────────────

function PaymentModal({ channel, onClose }: { channel: AdminChannelDto; onClose: () => void }) {
  const qc = useQueryClient()
  const today = format(new Date(), 'yyyy-MM-dd')
  const [paidFrom, setPaidFrom] = useState(today)
  const [paidUntil, setPaidUntil] = useState('')
  const [amount, setAmount] = useState('')
  const [comment, setComment] = useState('')

  const mut = useMutation({
    mutationFn: () =>
      adminNotificationsApi.markPayment(channel.id, {
        paidFrom,
        paidUntil,
        amount: Number(amount) || 0,
        comment: comment || undefined,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['admin-channels'] })
      qc.invalidateQueries({ queryKey: ['admin-channel-summary'] })
      onClose()
    },
  })

  return (
    <Modal title={`Отметить оплату — ${channel.ownerName}`} onClose={onClose}>
      <div className="flex flex-col gap-4">
        <div className="grid grid-cols-2 gap-3">
          <div className="flex flex-col gap-1.5">
            <label className="text-[13px] font-medium text-[#4A4038]">Оплачено с</label>
            <input
              type="date"
              value={paidFrom}
              onChange={(e) => setPaidFrom(e.target.value)}
              className="rounded-xl border border-line px-3 py-2.5 text-sm outline-none focus:border-gold"
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <label className="text-[13px] font-medium text-[#4A4038]">Оплачено до</label>
            <input
              type="date"
              value={paidUntil}
              onChange={(e) => setPaidUntil(e.target.value)}
              className="rounded-xl border border-line px-3 py-2.5 text-sm outline-none focus:border-gold"
            />
          </div>
        </div>
        <Input label="Сумма (справочно)" type="number" min={0} value={amount} onChange={(e) => setAmount(e.target.value)} />
        <div className="flex flex-col gap-1.5">
          <label className="text-[13px] font-medium text-[#4A4038]">Комментарий</label>
          <textarea
            value={comment}
            onChange={(e) => setComment(e.target.value)}
            rows={2}
            className="rounded-xl border border-line px-3 py-2 text-sm outline-none focus:border-gold resize-none"
            placeholder="Счёт 114"
          />
        </div>
        {mut.isError && <p className="text-sm text-danger">{getNotificationErrorMessage(mut.error)}</p>}
        <div className="flex gap-3 pt-1">
          <Button variant="secondary" className="flex-1" onClick={onClose}>
            Отмена
          </Button>
          <Button className="flex-1" disabled={!paidUntil} loading={mut.isPending} onClick={() => mut.mutate()}>
            Сохранить
          </Button>
        </div>
      </div>
    </Modal>
  )
}

// ── Channels list ─────────────────────────────────────────────────────────────

const STATE_LABEL_RU: Record<string, string> = {
  NotConnected: 'не подключён',
  Connecting: 'подключается',
  Connected: 'подключён',
  Disconnected: 'отвалился',
  Blocked: 'заблокирован',
  DisabledByOwner: 'отключён владельцем',
  NeedsReconnect: 'требует переподключения',
  Replaced: 'заменён',
}

function ChannelsList() {
  const [page, setPage] = useState(1)
  const [payingFor, setPayingFor] = useState<AdminChannelDto | null>(null)
  const [actionError, setActionError] = useState('')
  const qc = useQueryClient()

  const { data, isLoading } = useQuery({
    queryKey: ['admin-channels', page],
    queryFn: () => adminNotificationsApi.listChannels({ page, pageSize: 20 }),
  })

  const suspendMut = useMutation({
    mutationFn: (id: string) => adminNotificationsApi.suspend(id),
    onSuccess: () => {
      setActionError('')
      qc.invalidateQueries({ queryKey: ['admin-channels'] })
    },
    onError: (err: unknown) => setActionError(getNotificationErrorMessage(err)),
  })
  const resumeMut = useMutation({
    mutationFn: (id: string) => adminNotificationsApi.resume(id),
    onSuccess: () => {
      setActionError('')
      qc.invalidateQueries({ queryKey: ['admin-channels'] })
    },
    onError: (err: unknown) => setActionError(getNotificationErrorMessage(err)),
  })

  if (isLoading)
    return (
      <div className="grid gap-3">
        {Array.from({ length: 4 }).map((_, i) => (
          <div key={i} className="h-20 bg-cream-deep rounded-2xl animate-pulse" />
        ))}
      </div>
    )

  return (
    <div>
      {payingFor && <PaymentModal channel={payingFor} onClose={() => setPayingFor(null)} />}
      {actionError && <p className="text-sm text-danger mb-3">{actionError}</p>}
      <div className="grid gap-3">
        {(data?.items ?? []).map((c) => (
          <Card key={c.id} className="p-4 flex items-center justify-between gap-4 flex-wrap">
            <div>
              <div className="flex items-center gap-2 flex-wrap">
                <span className="font-medium text-ink">{c.ownerName}</span>
                <span className="text-xs text-muted">{c.ownerPhoneMasked}</span>
                <span className="text-xs px-2 py-0.5 rounded-full bg-cream-deep text-ink-soft">
                  {STATE_LABEL_RU[c.state] ?? c.state}
                </span>
                <span
                  className={`text-xs px-2 py-0.5 rounded-full font-medium ${
                    c.paymentState === 'Paid' ? 'bg-success-bg text-success' : 'bg-warning-bg text-warning'
                  }`}
                >
                  {c.paymentState === 'Paid' ? 'оплачен' : c.paymentState === 'Suspended' ? 'приостановлен' : 'не оплачен'}
                </span>
              </div>
              <p className="text-xs text-muted mt-0.5">
                {c.companyCount} компаний · оплачен до {fmt(c.paidUntil)}
                {c.requestedAt && <> · заявка от {fmt(c.requestedAt)}</>}
                {c.idleSince && <> · простой с {fmt(c.idleSince)}</>}
              </p>
            </div>
            <div className="flex gap-2 shrink-0">
              <Button size="sm" onClick={() => setPayingFor(c)}>
                Отметить оплату
              </Button>
              {c.paymentState === 'Paid' ? (
                <Button size="sm" variant="danger" loading={suspendMut.isPending} onClick={() => suspendMut.mutate(c.id)}>
                  Приостановить
                </Button>
              ) : c.paymentState === 'Suspended' ? (
                <Button size="sm" variant="secondary" loading={resumeMut.isPending} onClick={() => resumeMut.mutate(c.id)}>
                  Возобновить
                </Button>
              ) : null}
            </div>
          </Card>
        ))}
        {data?.items.length === 0 && (
          <Card className="p-10 text-center text-muted">
            <Icon name="megaphone" size={28} strokeWidth={1.6} className="mx-auto mb-2" />
            <p>Каналов пока нет</p>
          </Card>
        )}
      </div>
      {data && (
        <Pagination page={data.page} pageSize={data.pageSize} total={data.total} hasNext={data.hasNext} onPageChange={setPage} />
      )}
    </div>
  )
}

// ── Platform settings (price, idle days) ──────────────────────────────────────

function PlatformSettingsCard() {
  const qc = useQueryClient()
  const { data, isLoading } = useQuery({ queryKey: ['admin-platform-settings'], queryFn: adminNotificationsApi.getSettings })
  const [price, setPrice] = useState('')
  const [idleDays, setIdleDays] = useState('3')

  useEffect(() => {
    if (!data) return
    setPrice(data.channelPricePerMonth != null ? String(data.channelPricePerMonth) : '')
    setIdleDays(String(data.channelIdleDays))
  }, [data])

  const mut = useMutation({
    mutationFn: () =>
      adminNotificationsApi.updateSettings({
        channelPricePerMonth: price.trim() === '' ? null : Number(price),
        channelIdleDays: Number(idleDays),
      }),
    onSuccess: (res) => {
      qc.setQueryData(['admin-platform-settings'], res)
      qc.invalidateQueries({ queryKey: ['notification-channel-offer'] })
    },
  })

  if (isLoading) return <div className="h-32 bg-cream-deep rounded-2xl animate-pulse mb-5" />

  return (
    <Card className="p-6 mb-5">
      <h2 className="text-base font-semibold text-ink mb-1">Параметры опции «канал»</h2>
      <p className="text-sm text-muted mb-4">
        Пока цена не задана, подключение канала не предлагается владельцам (не показывается с нулём).
      </p>
      <div className="grid sm:grid-cols-2 gap-4">
        <Input
          label="Цена опции «канал» в месяц (₽)"
          type="number"
          min={0}
          placeholder="не задана"
          value={price}
          onChange={(e) => setPrice(e.target.value)}
        />
        <Input
          label="Срок простоя до отключения номера (дней)"
          type="number"
          min={0}
          max={60}
          value={idleDays}
          onChange={(e) => setIdleDays(e.target.value)}
        />
      </div>
      {mut.isError && <p className="text-sm text-danger mt-3">{getNotificationErrorMessage(mut.error)}</p>}
      {mut.isSuccess && <p className="text-sm text-success mt-3">Сохранено</p>}
      <Button className="mt-4" loading={mut.isPending} onClick={() => mut.mutate()}>
        Сохранить
      </Button>
    </Card>
  )
}

// ── Main tab ────────────────────────────────────────────────────────────────

export function NotificationsAdminTab() {
  return (
    <div>
      <SummaryRow />
      <PlatformSettingsCard />
      <ChannelsList />
    </div>
  )
}
