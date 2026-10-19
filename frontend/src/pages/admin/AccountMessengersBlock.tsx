import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { adminBillingApi, type AdminBillingAccount } from '../../api/adminBilling'
import { Button } from '../../components/ui/Button'
import { Input } from '../../components/ui/Input'
import { Modal } from '../../components/ui/Modal'
import { formatRubRounded } from '../../utils/money'
import type { AdminMessengerLine, MessengerDisableWhen } from '../../types'
import { adminErrorText } from './adminChannelsHelpers'
import { COMMENT_MAX, parseMonths } from './messengerOptionsHelpers'

// Cycle 43 (ARCHITECTURE_CYCLE43.md §43.6.6, API_CONTRACT_CYCLE43.md §43.25–§43.28): the «Мессенджеры» block of the billing-account card — two lines, always.
// Statuses, prices, totals, the reason a messenger cannot be connected and the confirmation texts are the server's; this file only collects input.

const STATUS_CLASS: Record<AdminMessengerLine['status'], string> = {
  Paid: 'bg-success-bg text-success',
  Trial: 'bg-info-bg text-info',
  Ending: 'bg-warning-bg text-warning',
  PendingPayment: 'bg-warning-bg text-warning',
  Expired: 'bg-danger-bg text-danger',
  NotConnected: 'bg-cream-deep text-ink-soft',
}

type CardAccount = Pick<AdminBillingAccount, 'id'> & Partial<Pick<AdminBillingAccount, 'messengers' | 'messengersMonthlyTotal'>>

/** The answer of extend / disable is the fresh account card: put it into the cache and refresh everything that shows the account. */
function useAccountCardUpdate(accountId: string) {
  const qc = useQueryClient()
  return (card: AdminBillingAccount) => {
    qc.setQueryData(['admin-billing-account', accountId], card)
    for (const key of ['admin-billing-accounts', 'admin-billing-account-history', 'admin-channels', 'admin-channel-card', 'admin-messenger-quote'])
      void qc.invalidateQueries({ queryKey: [key] })
  }
}

function ExtendDialog({ accountId, line, onClose }: { accountId: string; line: AdminMessengerLine; onClose: () => void }) {
  const qc = useQueryClient()
  const applyCard = useAccountCardUpdate(accountId)
  const [monthsRaw, setMonthsRaw] = useState('1')
  const [comment, setComment] = useState('')
  const months = parseMonths(monthsRaw)

  const quoteKey = ['admin-messenger-quote', accountId, line.transport, months] as const
  const quote = useQuery({
    queryKey: quoteKey,
    queryFn: () => adminBillingApi.messengerQuote(accountId, line.transport, months as number),
    enabled: months != null,
    retry: false,
    gcTime: 0,
  })
  const mut = useMutation({
    mutationFn: () =>
      adminBillingApi.extendMessenger(accountId, line.transport, {
        months: months as number,
        comment: comment.trim() || null,
        expectedPricePerMonth: (quote.data as NonNullable<typeof quote.data>).pricePerMonth,
      }),
    onSuccess: (card) => { applyCard(card); onClose() },
    // A changed price (409) means the shown calculation is stale: take a fresh one so the admin re-checks the new amount.
    onError: () => { void qc.invalidateQueries({ queryKey: ['admin-messenger-quote', accountId, line.transport] }) },
  })

  const monthsError = months == null ? 'Срок — от 1 до 24 месяцев' : undefined
  const busy = mut.isPending
  return (
    <Modal title={`${line.label}: ${line.extendActionText === 'Продлить' ? 'продлить' : 'подключить'}`} onClose={onClose} dismissible={!busy}>
      <form onSubmit={(e) => { e.preventDefault(); if (quote.data && !busy) mut.mutate() }} noValidate>
        <div className="grid gap-4">
          <Input label="На сколько месяцев (1–24)" type="number" min={1} max={24} value={monthsRaw} error={monthsError}
            onChange={(e) => { setMonthsRaw(e.target.value); mut.reset() }} />
          <Input label="Комментарий (счёт, платёж)" value={comment} maxLength={COMMENT_MAX} onChange={(e) => setComment(e.target.value)} />
        </div>
        <div className="mt-4 rounded-xl bg-cream-deep px-4 py-3 text-sm min-h-[3.5rem]" aria-live="polite">
          {months == null ? <span className="text-muted">Укажите срок, чтобы увидеть расчёт</span>
            : quote.isLoading || quote.isFetching ? <span className="text-muted">Считаем…</span>
            : quote.isError ? <span role="alert" className="text-danger">{adminErrorText(quote.error)}</span>
            : quote.data ? (
              <>
                <p className="font-medium text-ink">{quote.data.amountText}</p>
                <p className="text-ink-soft">{quote.data.newPaidUntilText}</p>
              </>
            ) : null}
        </div>
        {mut.isError && <p role="alert" className="text-sm text-danger mt-3">{adminErrorText(mut.error)}</p>}
        <div className="flex justify-end gap-2 mt-5">
          <Button type="button" variant="secondary" onClick={onClose} disabled={busy}>Отмена</Button>
          <Button type="submit" loading={busy} disabled={!quote.data || quote.isFetching || quote.isError}>
            {quote.data?.actionText ?? line.extendActionText}
          </Button>
        </div>
      </form>
    </Modal>
  )
}

function DisableDialog({ accountId, line, onClose }: { accountId: string; line: AdminMessengerLine; onClose: () => void }) {
  const applyCard = useAccountCardUpdate(accountId)
  const periodEndText = line.disablePeriodEndConfirmText
  const [when, setWhen] = useState<MessengerDisableWhen>('Now')
  const [comment, setComment] = useState('')
  const [touched, setTouched] = useState(false)
  const mut = useMutation({
    mutationFn: () => adminBillingApi.disableMessenger(accountId, line.transport, { when, comment: comment.trim() }),
    onSuccess: (card) => { applyCard(card); onClose() },
  })
  const commentError = touched && comment.trim() === '' ? 'Укажите причину отключения' : undefined
  const confirmText = when === 'Now' ? line.disableNowConfirmText : periodEndText
  return (
    <Modal title={`${line.label}: отключить`} onClose={onClose} dismissible={!mut.isPending}>
      <form onSubmit={(e) => { e.preventDefault(); setTouched(true); if (comment.trim() !== '') mut.mutate() }} noValidate>
        <fieldset className="grid gap-2 mb-4">
          <legend className="text-[13px] font-medium text-[#4A4038] mb-1.5">Когда отключить</legend>
          <label className="flex items-center gap-2 text-sm text-ink cursor-pointer">
            <input type="radio" name="disable-when" className="accent-gold" checked={when === 'Now'} onChange={() => setWhen('Now')} /> Сейчас
          </label>
          {periodEndText != null && (
            <label className="flex items-center gap-2 text-sm text-ink cursor-pointer">
              <input type="radio" name="disable-when" className="accent-gold" checked={when === 'PeriodEnd'} onChange={() => setWhen('PeriodEnd')} /> По окончании оплаченного периода
            </label>
          )}
        </fieldset>
        <Input label="Причина отключения" value={comment} maxLength={COMMENT_MAX} error={commentError} onChange={(e) => setComment(e.target.value)} onBlur={() => setTouched(true)} />
        {confirmText && <p className="mt-4 rounded-xl bg-warning-bg text-warning px-4 py-3 text-sm font-medium">{confirmText}</p>}
        {mut.isError && <p role="alert" className="text-sm text-danger mt-3">{adminErrorText(mut.error)}</p>}
        <div className="flex justify-end gap-2 mt-5">
          <Button type="button" variant="secondary" onClick={onClose} disabled={mut.isPending}>Отмена</Button>
          <Button type="submit" variant="danger" loading={mut.isPending}>Отключить</Button>
        </div>
      </form>
    </Modal>
  )
}

function Line({ line, onExtend, onDisable }: { line: AdminMessengerLine; onExtend: () => void; onDisable: () => void }) {
  return (
    <div className="px-3 py-3" data-testid={`messenger-line-${line.transport}`}>
      <div className="flex items-start justify-between gap-3 flex-wrap">
        <div className="min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <span className="text-sm font-medium text-ink">{line.label}</span>
            <span className={`text-xs font-medium px-2 py-0.5 rounded-full ${STATUS_CLASS[line.status]}`}>{line.statusText}</span>
          </div>
          <p className="text-xs text-muted mt-1">{line.priceText} · {line.number.text}</p>
          {!line.canExtend && line.extendDisabledText && <p className="text-xs text-warning mt-1">{line.extendDisabledText}</p>}
        </div>
        <div className="flex items-center gap-3 shrink-0">
          <span className="text-sm text-ink">{line.countsInTotal ? formatRubRounded(line.monthlyPrice) : '—'}</span>
          <Button size="sm" variant="secondary" disabled={!line.canExtend} onClick={onExtend} aria-label={`${line.extendActionText} ${line.label}`}>{line.extendActionText}</Button>
          {line.canDisable && <Button size="sm" variant="ghost" onClick={onDisable} aria-label={`Отключить ${line.label}`}>Отключить</Button>}
        </div>
      </div>
    </div>
  )
}

export function AccountMessengersBlock({ account }: { account: CardAccount }) {
  const [extending, setExtending] = useState<AdminMessengerLine['transport'] | null>(null)
  const [disabling, setDisabling] = useState<AdminMessengerLine['transport'] | null>(null)
  const lines = account.messengers
  if (!lines || lines.length === 0) return null
  const extendLine = lines.find((l) => l.transport === extending)
  const disableLine = lines.find((l) => l.transport === disabling)
  return (
    <div data-testid="account-messengers">
      <p className="text-sm font-medium text-ink-soft mb-1.5">Мессенджеры</p>
      <div className="rounded-xl border border-line divide-y divide-line">
        {lines.map((l) => (
          <Line key={l.transport} line={l} onExtend={() => setExtending(l.transport)} onDisable={() => setDisabling(l.transport)} />
        ))}
        <div className="flex items-center justify-between px-3 py-2 text-sm bg-cream-deep">
          <span className="text-ink-soft">Мессенджеры в месяц</span>
          <span className="font-semibold text-ink">{formatRubRounded(account.messengersMonthlyTotal ?? 0)}</span>
        </div>
      </div>
      {extendLine && <ExtendDialog key={extendLine.transport} accountId={account.id} line={extendLine} onClose={() => setExtending(null)} />}
      {disableLine && <DisableDialog key={disableLine.transport} accountId={account.id} line={disableLine} onClose={() => setDisabling(null)} />}
    </div>
  )
}
