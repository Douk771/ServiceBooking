import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Modal } from '../ui/Modal'
import { Button } from '../ui/Button'
import { clientConsentsApi, type WrittenHealthConsentRevokeReason } from '../../api/clientConsents'
import { getLastPrintedHealthForm } from '../../utils/healthConsentForm'
import { getMarkWrittenConsentErrorMessage, getRevokeWrittenConsentErrorMessage } from '../../utils/healthConsentError'
import { healthConsentFormPrintPath } from '../../utils/clientKey'
import { HEALTH_FORM_NO_PHOTO_NOTICE } from '../../legal/staffNotices'

const FORM_ID_PATTERN = /^HD-[0-9A-HJKMNP-TV-Z]{8}$/

/**
 * API_CONTRACT_CYCLE20.md §432.5 (US-20-01, Т20-03) — "Бланк подписан, оригинал у нас". The staff
 * member confirms the checkbox AND the `formId` of the bланк they printed (or explicitly says it's
 * the salon's own paper form). `currentFormVersion` comes from the SAME `GET …/health-note` response
 * that decided the field was closed — no separate fetch needed just to learn it (§432.1 always
 * includes `writtenConsent.currentFormVersion`, granted or not).
 */
export function MarkWrittenConsentDialog({
  companyId,
  clientKey,
  currentFormVersion,
  onSuccess,
  onClose,
}: {
  companyId: string
  clientKey: string
  currentFormVersion: string
  onSuccess: () => void
  onClose: () => void
}) {
  const lastPrinted = getLastPrintedHealthForm(companyId, clientKey)
  const prefillFormId = lastPrinted && lastPrinted.textVersion === currentFormVersion ? lastPrinted.formId : null
  const [ownForm, setOwnForm] = useState(!prefillFormId)
  const [formId, setFormId] = useState(prefillFormId ?? '')
  const [confirmed, setConfirmed] = useState(false)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [outdated, setOutdated] = useState(false)

  const formIdInvalid = !ownForm && formId.length > 0 && !FORM_ID_PATTERN.test(formId)
  const canSubmit = confirmed && !formIdInvalid

  const submit = async () => {
    setLoading(true)
    setError('')
    try {
      await clientConsentsApi.markWrittenConsent(companyId, clientKey, {
        textVersion: currentFormVersion,
        formId: ownForm ? null : formId || null,
        confirmed: true,
      })
      onSuccess()
    } catch (err) {
      const mapped = getMarkWrittenConsentErrorMessage(err)
      setError(mapped.message)
      setOutdated(mapped.outdatedForm)
    } finally {
      setLoading(false)
    }
  }

  return (
    <Modal title="Бланк подписан, оригинал у нас" onClose={onClose}>
      <div className="flex flex-col gap-4">
        <p className="text-sm text-ink-soft">
          Отмечая это, вы подтверждаете, что клиент подписал письменное согласие на бланке и подписанный оригинал остаётся в салоне.
        </p>
        <p className="text-xs text-danger bg-danger-bg rounded-lg px-3 py-2">{HEALTH_FORM_NO_PHOTO_NOTICE}</p>

        <label className="flex items-center gap-2.5 cursor-pointer">
          <input type="checkbox" checked={ownForm} onChange={(e) => setOwnForm(e.target.checked)} className="w-4 h-4 accent-gold" />
          <span className="text-sm text-ink-soft">Использован собственный бланк салона (не распечатанный в сервисе)</span>
        </label>

        {!ownForm && (
          <div className="flex flex-col gap-1.5">
            <label className="text-[13px] font-medium text-[#4A4038]">Номер бланка</label>
            <input
              value={formId}
              onChange={(e) => setFormId(e.target.value.toUpperCase())}
              placeholder="HD-7K3M9QTX"
              className="rounded-xl border border-line px-3 py-2.5 text-sm outline-none focus:border-gold bg-white text-ink font-mono"
            />
            {formIdInvalid && <p className="text-xs text-danger">Формат номера — HD- и 8 символов, как на распечатанном бланке.</p>}
            <Link to={healthConsentFormPrintPath(companyId, clientKey)} className="text-xs text-gold-dark underline w-fit">
              Распечатать бланк заново
            </Link>
          </div>
        )}

        <label className="flex items-start gap-2.5 cursor-pointer bg-cream-deep rounded-xl px-3 py-2.5">
          <input type="checkbox" checked={confirmed} onChange={(e) => setConfirmed(e.target.checked)} className="w-4 h-4 accent-gold mt-0.5" />
          <span className="text-sm text-ink">Подписанный оригинал у нас</span>
        </label>

        {error && (
          <div className="text-sm text-danger flex flex-col gap-1.5">
            <p>{error}</p>
            {outdated && (
              <Link to={healthConsentFormPrintPath(companyId, clientKey)} className="underline w-fit">
                Распечатать бланк заново
              </Link>
            )}
          </div>
        )}

        <div className="flex gap-3 pt-1">
          <Button variant="secondary" className="flex-1" onClick={onClose}>
            Отмена
          </Button>
          <Button className="flex-1" disabled={!canSubmit} loading={loading} onClick={submit}>
            Отметить
          </Button>
        </div>
      </div>
    </Modal>
  )
}

const REVOKE_REASON_LABEL: Record<WrittenHealthConsentRevokeReason, string> = {
  SubjectWithdrew: 'Клиент отозвал согласие',
  MarkedByMistake: 'Отметка поставлена ошибочно',
}

/**
 * API_CONTRACT_CYCLE20.md §432.6 — snapping the mark ALSO deletes the health note in the same
 * transaction (`healthNotesDeleted`), so the confirmation step warns about that BEFORE the call, not
 * after (§441 item 4).
 */
export function RevokeWrittenConsentDialog({
  companyId,
  clientKey,
  onSuccess,
  onClose,
}: {
  companyId: string
  clientKey: string
  onSuccess: () => void
  onClose: () => void
}) {
  const [reason, setReason] = useState<WrittenHealthConsentRevokeReason>('SubjectWithdrew')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const submit = async () => {
    setLoading(true)
    setError('')
    try {
      await clientConsentsApi.revokeWrittenConsent(companyId, clientKey, reason)
      onSuccess()
    } catch (err) {
      setError(getRevokeWrittenConsentErrorMessage(err))
    } finally {
      setLoading(false)
    }
  }

  return (
    <Modal title="Снять отметку о письменном согласии" onClose={onClose}>
      <div className="flex flex-col gap-4">
        <p className="text-sm text-danger bg-danger-bg rounded-xl px-3 py-2.5">
          Запись о здоровье этого клиента в этой компании будет удалена без возможности восстановления.
        </p>
        <div className="flex flex-col gap-1.5">
          <label className="text-[13px] font-medium text-[#4A4038]">Причина</label>
          <select
            value={reason}
            onChange={(e) => setReason(e.target.value as WrittenHealthConsentRevokeReason)}
            className="rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold bg-white text-ink"
          >
            {Object.entries(REVOKE_REASON_LABEL).map(([v, l]) => (
              <option key={v} value={v}>
                {l}
              </option>
            ))}
          </select>
        </div>
        {error && <p className="text-sm text-danger">{error}</p>}
        <div className="flex gap-3 pt-1">
          <Button variant="secondary" className="flex-1" onClick={onClose}>
            Отмена
          </Button>
          <Button variant="danger" className="flex-1" loading={loading} onClick={submit}>
            Снять отметку
          </Button>
        </div>
      </div>
    </Modal>
  )
}
