import { useEffect, useRef, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { notificationChannelsApi } from '../../api/notificationChannels'
import { NUMBERS_OVERVIEW_QUERY_KEY, type NumberChannelDto, type TransportNumbersDto } from '../../api/notificationNumbers'
import { Button } from '../ui/Button'
import { Icon } from '../ui/Icon'
import { Modal } from '../ui/Modal'
import { getNotificationErrorMessage } from '../../utils/notificationError'
import * as T from './numbersTexts'

interface Props {
  transport: TransportNumbersDto
  /** Лишний номер (`extraChannels`): только отвязка, без мастера. */
  extraChannel?: NumberChannelDto
  onOpenWizard: (t: TransportNumbersDto) => void
}

const STATUS_STYLE = {
  Working: 'bg-success-bg text-success',
  ActionRequired: 'bg-warning-bg text-warning',
  Off: 'bg-cream-deep text-ink-soft',
} as const

type Confirm = 'replace' | 'unbind' | null

/** Строка одного транспорта. Статус, текст и единственная кнопка — поля сервера (displayStatus/displayText/action), без вычислений. */
export function NumberRow({ transport, extraChannel, onOpenWizard }: Props) {
  const qc = useQueryClient()
  const channel = extraChannel ?? transport.channel ?? null
  const [menuOpen, setMenuOpen] = useState(false)
  const [confirm, setConfirm] = useState<Confirm>(null)
  const [error, setError] = useState('')
  const menuRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!menuOpen) return
    const onDoc = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setMenuOpen(false)
    }
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setMenuOpen(false)
    document.addEventListener('mousedown', onDoc)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onDoc)
      document.removeEventListener('keydown', onKey)
    }
  }, [menuOpen])

  const done = () => {
    setConfirm(null)
    setError('')
    void qc.invalidateQueries({ queryKey: NUMBERS_OVERVIEW_QUERY_KEY })
    void qc.invalidateQueries({ queryKey: ['notification-channels'] })
  }
  const fail = (e: unknown) => {
    setConfirm(null)
    setError(getNotificationErrorMessage(e))
  }
  const replaceMut = useMutation({ mutationFn: (id: string) => notificationChannelsApi.replace(id), onSuccess: done, onError: fail })
  const unbindMut = useMutation({ mutationFn: (id: string) => notificationChannelsApi.disconnect(id), onSuccess: done, onError: fail })

  const status = extraChannel ? extraChannel.displayStatus : (channel?.displayStatus ?? transport.displayStatus)
  const text = extraChannel ? extraChannel.displayText : (channel?.displayText ?? transport.displayText)
  const action = extraChannel ? extraChannel.action : (channel?.action ?? transport.action)
  const canReplace = !!channel && channel.canReplace
  const canUnbind = !!channel && channel.state !== 'Replaced'

  const secondary = !extraChannel && !channel && !transport.paid && !transport.sellable ? transport.unavailableText : null
  const showPrimary = action && (action !== 'Pay' || transport.canRequestPayment)

  const onPrimary = () => {
    if (!action) return
    if (action === 'ReplaceNumber') setConfirm('replace')
    else if (action === 'Unbind') setConfirm('unbind')
    else onOpenWizard(transport)
  }

  const busy = replaceMut.isPending || unbindMut.isPending
  const runConfirmed = () => {
    if (!channel) return
    if (confirm === 'replace') replaceMut.mutate(channel.id)
    else if (confirm === 'unbind') unbindMut.mutate(channel.id)
  }

  return (
    <div className="px-5 py-4 flex flex-col gap-2">
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div className="min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <p className="font-semibold text-ink">{transport.displayName}</p>
            {channel?.phoneMasked && <span className="text-sm text-ink-soft">{channel.phoneMasked}</span>}
            {status && (
              <span className={`text-[11px] font-medium px-2 py-0.5 rounded-full ${STATUS_STYLE[status]}`}>
                {status === 'Working' ? 'Работает' : status === 'ActionRequired' ? 'Нужно действие' : 'Выключен'}
              </span>
            )}
          </div>
          {text && <p className="text-sm text-ink-soft mt-0.5">{text}</p>}
          {secondary && <p className="text-sm text-muted mt-0.5">{secondary}</p>}
          {channel?.fundingText && <p className="text-xs text-muted mt-1">{channel.fundingText}</p>}
          {!extraChannel && transport.priceText && !transport.paid && <p className="text-xs text-muted mt-1">{transport.priceText}</p>}
          {!extraChannel && channel?.lastTest && <p className="text-xs text-muted mt-1">{channel.lastTest.text}</p>}
        </div>

        <div className="flex items-center gap-2">
          {showPrimary && (
            <Button size="sm" onClick={onPrimary}>
              {T.ACTION_LABELS[action]}
            </Button>
          )}
          {(canReplace || canUnbind) && (
            <div className="relative" ref={menuRef}>
              <button
                type="button"
                aria-label={T.MENU_LABEL}
                aria-haspopup="menu"
                aria-expanded={menuOpen}
                onClick={() => setMenuOpen((o) => !o)}
                className="w-8 h-8 rounded-full flex items-center justify-center text-ink-soft hover:bg-cream-deep"
              >
                <span aria-hidden className="text-lg leading-none tracking-widest -mt-1">…</span>
              </button>
              {menuOpen && (
                <div role="menu" className="absolute right-0 mt-1 z-20 min-w-[180px] bg-white border border-line rounded-xl shadow-modal py-1">
                  {canReplace && (
                    <button role="menuitem" className="w-full text-left px-4 py-2 text-sm text-ink hover:bg-cream-deep" onClick={() => { setMenuOpen(false); setConfirm('replace') }}>
                      {T.MENU_REPLACE}
                    </button>
                  )}
                  {canUnbind && (
                    <button role="menuitem" className="w-full text-left px-4 py-2 text-sm text-danger hover:bg-cream-deep" onClick={() => { setMenuOpen(false); setConfirm('unbind') }}>
                      {T.MENU_UNBIND}
                    </button>
                  )}
                </div>
              )}
            </div>
          )}
        </div>
      </div>

      {error && <p role="alert" className="text-xs text-danger flex items-center gap-1"><Icon name="alert-circle" size={12} /> {error}</p>}

      {confirm && (
        <Modal title={confirm === 'replace' ? T.MENU_REPLACE : T.MENU_UNBIND} onClose={() => setConfirm(null)} dismissible={!busy}>
          <div className="flex flex-col gap-4">
            <p className="text-sm text-ink-soft">{confirm === 'replace' ? T.CONFIRM_REPLACE_TEXT : T.CONFIRM_UNBIND_TEXT}</p>
            <div className="flex gap-3">
              <Button variant="secondary" className="flex-1" disabled={busy} onClick={() => setConfirm(null)}>{T.CANCEL}</Button>
              <Button variant={confirm === 'unbind' ? 'danger' : 'primary'} className="flex-1" loading={busy} onClick={runConfirmed}>
                {confirm === 'replace' ? T.MENU_REPLACE : T.MENU_UNBIND}
              </Button>
            </div>
          </div>
        </Modal>
      )}
    </div>
  )
}
