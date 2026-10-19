import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { adminBillingApi } from '../../api/adminBilling'
import { Button } from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { Input } from '../../components/ui/Input'
import { Modal } from '../../components/ui/Modal'
import { fmtDateTime } from '../../utils/dateFormat'
import type { AdminMessengerOption, MessengerPricePreview } from '../../types'
import { MESSENGER_OPTIONS_QUERY_KEY, useMessengerOptions } from './useMessengerOptions'
import { adminErrorText } from './adminChannelsHelpers'
import {
  buildPreviewBody,
  buildSaveBody,
  COMMENT_MAX,
  historyLine,
  parsePriceInput,
  priceToInput,
  validateComment,
} from './messengerOptionsHelpers'

// Cycle 43 (ARCHITECTURE_CYCLE43.md §43.3.7, API_CONTRACT_CYCLE43.md §43.22–§43.24): the price of the two messenger options, the sale verdict and the
// price journal. The frontend computes nothing: the verdict, the consequences and the confirmation texts are the server's; errors are printed verbatim.

function PriceModal({ option, onClose }: { option: AdminMessengerOption; onClose: () => void }) {
  const qc = useQueryClient()
  const [price, setPrice] = useState(priceToInput(option.pricePerMonth))
  const [isActive, setIsActive] = useState(option.isActive)
  const [comment, setComment] = useState('')
  const [touched, setTouched] = useState(false)
  const [preview, setPreview] = useState<MessengerPricePreview | null>(null)

  const parsed = parsePriceInput(price)
  const priceError = parsed.ok ? undefined : parsed.error
  const commentError = touched ? validateComment(comment) ?? undefined : undefined

  const previewMut = useMutation({
    mutationFn: (value: number | null) => adminBillingApi.previewMessengerPrice(option.transport, buildPreviewBody(option, value, isActive)),
    onSuccess: setPreview,
  })
  const saveMut = useMutation({
    mutationFn: (value: number | null) => adminBillingApi.updateMessengerPrice(option.transport, buildSaveBody(option, value, isActive, comment)),
    onSuccess: (fresh) => {
      qc.setQueryData(MESSENGER_OPTIONS_QUERY_KEY, (old: { items: AdminMessengerOption[] } | undefined) =>
        old ? { ...old, items: old.items.map((i) => (i.transport === fresh.transport ? fresh : i)) } : old,
      )
      void qc.invalidateQueries({ queryKey: MESSENGER_OPTIONS_QUERY_KEY })
      for (const key of ['admin-billing-accounts', 'admin-billing-account', 'admin-options', 'admin-channels', 'public-pricing', 'notification-numbers'])
        void qc.invalidateQueries({ queryKey: [key] })
      onClose()
    },
  })

  const busy = previewMut.isPending || saveMut.isPending

  const onContinue = () => {
    setTouched(true)
    if (!parsed.ok || validateComment(comment)) return
    previewMut.mutate(parsed.value)
  }

  return (
    <Modal title={`${option.label}: цена и продажа`} onClose={onClose} dismissible={!busy}>
      {preview && parsed.ok ? (
        <div>
          <h3 className="text-base font-semibold text-ink mb-3">{preview.confirmTitle}</h3>
          {preview.consequences.length > 0 && (
            <ul className="grid gap-2 mb-4" aria-label="Последствия">
              {preview.consequences.map((c, i) => (
                <li key={i} className={`text-sm rounded-xl px-3.5 py-2.5 ${preview.isZeroPrice && c.startsWith('Цена 0') ? 'bg-warning-bg text-warning font-medium' : 'bg-cream-deep text-ink-soft'}`}>
                  {c}
                </li>
              ))}
            </ul>
          )}
          {saveMut.isError && <p role="alert" className="text-sm text-danger mb-3">{adminErrorText(saveMut.error)}</p>}
          <div className="flex justify-end gap-2 mt-5">
            <Button variant="secondary" onClick={() => { setPreview(null); saveMut.reset() }} disabled={busy}>Назад</Button>
            <Button loading={saveMut.isPending} onClick={() => saveMut.mutate(parsed.value)}>{preview.confirmButtonText}</Button>
          </div>
        </div>
      ) : (
        <form onSubmit={(e) => { e.preventDefault(); onContinue() }} noValidate>
          <div className="grid gap-4">
            <Input
              label="Цена за месяц, ₽ (пусто — снять цену)"
              inputMode="decimal"
              value={price}
              error={priceError}
              onChange={(e) => { setPrice(e.target.value); previewMut.reset() }}
            />
            <label className="flex items-center gap-2 text-sm text-ink cursor-pointer">
              <input type="checkbox" className="accent-gold" checked={isActive} onChange={(e) => { setIsActive(e.target.checked); previewMut.reset() }} />
              Опция активна
            </label>
            <Input
              label="Причина изменения"
              value={comment}
              maxLength={COMMENT_MAX}
              error={commentError}
              onChange={(e) => setComment(e.target.value)}
              onBlur={() => setTouched(true)}
            />
          </div>
          {previewMut.isError && <p role="alert" className="text-sm text-danger mt-3">{adminErrorText(previewMut.error)}</p>}
          <div className="flex justify-end gap-2 mt-5">
            <Button type="button" variant="secondary" onClick={onClose} disabled={busy}>Отмена</Button>
            <Button type="submit" loading={previewMut.isPending}>Продолжить</Button>
          </div>
        </form>
      )}
    </Modal>
  )
}

function OptionRow({ option, onEdit }: { option: AdminMessengerOption; onEdit: () => void }) {
  return (
    <div className="py-4 first:pt-0 last:pb-0">
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div className="min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <h3 className="text-sm font-semibold text-ink">{option.label}</h3>
            <span className={`text-xs font-medium px-2 py-0.5 rounded-full ${option.sale.sellable ? 'bg-success-bg text-success' : 'bg-cream-deep text-ink-soft'}`}>
              {option.sale.text}
            </span>
          </div>
          <p className="text-sm text-ink mt-1">
            {option.priceText}
            <span className="text-muted"> · {option.isActive ? 'опция активна' : 'опция неактивна'} · {option.open ? 'открыт для владельцев' : 'закрыт для владельцев'}</span>
          </p>
          <p className="text-xs text-muted mt-0.5">Аккаунтов с действующей оплатой: {option.activePaidAccounts}</p>
        </div>
        <Button size="sm" variant="secondary" onClick={onEdit} aria-label={`Изменить цену ${option.label}`}>Изменить цену</Button>
      </div>
      {option.history.length > 0 && (
        <details className="mt-3">
          <summary className="text-xs text-ink-soft cursor-pointer select-none">Журнал изменений ({option.history.length})</summary>
          <ul className="grid gap-1 mt-2" aria-label={`Журнал цены ${option.label}`}>
            {option.history.map((h, i) => (
              <li key={i} className="text-xs text-ink-soft">{historyLine(h, fmtDateTime)}</li>
            ))}
          </ul>
        </details>
      )}
    </div>
  )
}

export function MessengerOptionsPanel() {
  const { data, isLoading, isError, refetch } = useMessengerOptions()
  const [editing, setEditing] = useState<AdminMessengerOption['transport'] | null>(null)

  if (isLoading) return <div className="h-40 bg-cream-deep rounded-2xl animate-pulse mb-5" aria-label="Загрузка цен мессенджеров" />
  if (isError || !data)
    return (
      <Card className="p-6 mb-5">
        <p role="alert" className="text-sm text-danger mb-3">Не удалось загрузить цены мессенджеров.</p>
        <Button size="sm" variant="secondary" onClick={() => void refetch()}>Повторить</Button>
      </Card>
    )

  const option = data.items.find((i) => i.transport === editing)
  return (
    <Card className="p-6 mb-5">
      <h2 className="text-base font-semibold text-ink mb-1">Цена мессенджеров</h2>
      <p className="text-sm text-muted mb-4">{data.priceHint}</p>
      <div className="divide-y divide-line">
        {data.items.map((o) => (
          <OptionRow key={o.transport} option={o} onEdit={() => setEditing(o.transport)} />
        ))}
      </div>
      {option && <PriceModal key={option.transport} option={option} onClose={() => setEditing(null)} />}
    </Card>
  )
}
