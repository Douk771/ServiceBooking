import { useEffect, useState } from 'react'
import { Icon } from '../ui/Icon'
import { Button } from '../ui/Button'
import { VerificationStatus } from './VerificationStatus'
import type { PhoneVerificationSessionCreated, PhoneVerificationSessionStatus } from '../../types'

interface VerifyPhoneDialogProps {
  session: PhoneVerificationSessionCreated
  status: PhoneVerificationSessionStatus | null
  statusError?: string | null
  onClose: () => void
  onRestart: () => void
}

/**
 * ARCHITECTURE_CYCLE14.md §163, T14-F3 (US-14-02) — mobile gets a direct "open the bot" link, desktop
 * gets a QR code, and BOTH always show the deep link as selectable/copyable text next to it: that text
 * is the accessible fallback (screen reader, QR camera unavailable, desktop-without-phone-nearby) and
 * is never hidden behind either device branch.
 *
 * Device targeting is done with a CSS breakpoint (`sm:`), not UA sniffing — consistent with the rest
 * of the app's responsive components and avoids a wrong guess on unusual browsers.
 */
export function VerifyPhoneDialog({ session, status, statusError, onClose, onRestart }: VerifyPhoneDialogProps) {
  const [copied, setCopied] = useState(false)

  useEffect(() => {
    if (!copied) return
    const t = setTimeout(() => setCopied(false), 2000)
    return () => clearTimeout(t)
  }, [copied])

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(session.deepLink)
      setCopied(true)
    } catch {
      // Clipboard API unavailable/denied — the link is still selectable as plain text below.
    }
  }

  const handleKeyDown: React.KeyboardEventHandler<HTMLDivElement> = (e) => {
    if (e.key === 'Escape') onClose()
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="verify-phone-dialog-title"
      className="fixed inset-0 z-50 flex items-center justify-center bg-ink/40 px-4 py-8"
      onClick={onClose}
      onKeyDown={handleKeyDown}
    >
      <div
        className="bg-white rounded-3xl border border-line shadow-soft max-w-[420px] w-full p-7 max-h-full overflow-y-auto"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between gap-3 mb-1">
          <h2 id="verify-phone-dialog-title" className="font-serif text-xl text-ink">
            Подтверждение номера
          </h2>
          <button
            type="button"
            onClick={onClose}
            aria-label="Закрыть"
            className="shrink-0 text-muted hover:text-ink transition-colors"
          >
            <Icon name="x" size={18} strokeWidth={1.8} />
          </button>
        </div>
        <p className="text-sm text-ink-soft mb-5">
          Номер {session.phoneMasked}. Откройте бота MAX и поделитесь своим контактом — он подставится автоматически,
          нужно только подтвердить.
        </p>

        {/* Mobile — a direct CTA to open the bot. */}
        <a
          href={session.deepLink}
          target="_blank"
          rel="noreferrer"
          className="sm:hidden w-full inline-flex items-center justify-center gap-2 font-semibold rounded-full bg-ink hover:bg-ink/90 text-cream px-5 py-2.5 text-sm mb-4"
        >
          Открыть MAX
        </a>

        {/* Desktop — QR code, when the server provided one. */}
        {session.qrPngBase64 && (
          <div className="hidden sm:flex flex-col items-center gap-3 mb-4">
            <img
              src={`data:image/png;base64,${session.qrPngBase64}`}
              alt={`QR-код для перехода к боту MAX и подтверждения номера ${session.phoneMasked}`}
              width={200}
              height={200}
              className="rounded-2xl border border-line"
            />
            <p className="text-xs text-muted text-center">Отсканируйте QR-код камерой телефона</p>
          </div>
        )}

        {/* Кто пользуется MAX в браузере, а не в приложении: страница max.ru предлагает только
            max://-схему, и в Safari без установленного MAX это ошибка вместо перехода к боту.
            Найдено на первом живом проходе 24.09.2026. */}
        {session.webLink && (
          <p className="text-xs text-muted mb-4">
            Пользуетесь MAX в браузере?{' '}
            <a
              href={session.webLink}
              target="_blank"
              rel="noreferrer"
              className="text-ink underline underline-offset-2 hover:no-underline"
            >
              Откройте бота в веб-версии
            </a>
            .
          </p>
        )}

        {/* Always present, on both breakpoints — the accessible/copyable fallback (US-14-02). */}
        <div className="flex items-center gap-2 mb-5">
          <label htmlFor="verify-phone-deep-link" className="sr-only">
            Ссылка на бота MAX
          </label>
          <input
            id="verify-phone-deep-link"
            type="text"
            readOnly
            value={session.deepLink}
            onFocus={(e) => e.currentTarget.select()}
            className="flex-1 min-w-0 rounded-xl border border-line px-3 py-2 text-xs text-ink-soft bg-cream-deep/40 outline-none"
          />
          <Button type="button" variant="secondary" size="sm" onClick={handleCopy} aria-label="Скопировать ссылку">
            <Icon name="copy" size={14} strokeWidth={1.8} />
            {copied ? 'Скопировано' : 'Копировать'}
          </Button>
        </div>

        <VerificationStatus status={status} onRestart={onRestart} statusError={statusError} />

        {status?.status === 'Verified' && (
          <Button type="button" onClick={onClose} className="w-full mt-5">
            Готово
          </Button>
        )}
      </div>
    </div>
  )
}
