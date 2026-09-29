import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import { clientConsentsApi } from '../../api/clientConsents'
import { Button } from '../../components/ui/Button'
import { Icon } from '../../components/ui/Icon'
import { MarkWrittenConsentDialog, RevokeWrittenConsentDialog } from './HealthWrittenConsentDialogs'
import { isRequiredWrittenConsent, getHealthNoteSaveErrorMessage } from '../../utils/healthConsentError'
import { healthConsentFormPrintPath } from '../../utils/clientKey'
import { HEALTH_FORM_NO_PHOTO_NOTICE } from '../../legal/staffNotices'

interface Props {
  companyId: string
  clientKey: string
}

/**
 * "Противопоказания и особенности здоровья" — API_CONTRACT_CYCLE5.md §45; ARCHITECTURE_CYCLE5.md
 * T5-F5, US-77. Visually separate from the ordinary note (own card, own colour), never mixed into it.
 * `MasterClientsPage` doesn't render this component at all for SuperAdmin — the requirement is that
 * the interface show no block, not an empty/403 one (§45.1, checklist item in §53).
 *
 * Rewritten for cycle 20 (LG1, API_CONTRACT_CYCLE20.md §432): the field is gated ONLY by the written
 * (paper-form) consent mark — `writtenConsent.granted` — never by the salon's electronic consent,
 * which no longer opens anything (§432.1). "Заполнить"/edit only ever appears once a mark exists;
 * without one the card shows the print/mark flow instead of a bare "заполните согласие" message.
 */
export function HealthNoteCard({ companyId, clientKey }: Props) {
  const qc = useQueryClient()
  const [editing, setEditing] = useState(false)
  const [value, setValue] = useState('')
  const [showMarkDialog, setShowMarkDialog] = useState(false)
  const [showRevokeDialog, setShowRevokeDialog] = useState(false)
  const [saveError, setSaveError] = useState('')

  const queryKey = ['health-note', companyId, clientKey]
  const { data, isLoading } = useQuery({ queryKey, queryFn: () => clientConsentsApi.getHealthNote(companyId, clientKey) })

  const invalidate = () => qc.invalidateQueries({ queryKey })

  const saveMut = useMutation({
    mutationFn: (v: string) => clientConsentsApi.updateHealthNote(companyId, clientKey, v),
    onSuccess: () => {
      setEditing(false)
      setSaveError('')
      invalidate()
    },
    onError: (err: unknown) => {
      // §432.2 — the one 400 with a JSON body (`requiredTextKey`). A race (mark revoked by someone
      // else between opening the editor and saving) re-closes the field instead of showing a bare
      // error the staff member can't act on from here.
      if (isRequiredWrittenConsent(err)) {
        setEditing(false)
        invalidate()
      } else {
        setSaveError(getHealthNoteSaveErrorMessage(err))
      }
    },
  })

  const deleteMut = useMutation({
    mutationFn: () => clientConsentsApi.deleteHealthNote(companyId, clientKey),
    onSuccess: () => {
      setEditing(false)
      invalidate()
    },
  })

  if (isLoading) return <div className="h-16 bg-cream-deep rounded-xl animate-pulse" />

  const written = data?.writtenConsent
  const granted = written?.granted ?? false
  const currentFormVersion = written?.currentFormVersion ?? ''
  const formOutdated = granted && !!written?.formVersion && written.formVersion !== currentFormVersion

  return (
    <div className="rounded-xl border border-[#EAC5B9] bg-[#FBF3EF] px-3.5 py-3">
      <div className="flex items-center gap-1.5 mb-1.5">
        <Icon name="alert-circle" size={13} strokeWidth={1.8} className="text-[#B5624A]" />
        <p className="text-xs font-semibold text-[#8A4632] uppercase tracking-wide">Противопоказания и особенности здоровья</p>
      </div>

      {!granted ? (
        <div>
          <p className="text-xs text-[#8A4632]">
            Поле закрыто. Нужна отметка «письменное согласие получено» — клиент подписывает бумажный бланк.
          </p>
          <p className="text-[11px] text-[#B5624A] mt-1">{HEALTH_FORM_NO_PHOTO_NOTICE}</p>
          <div className="flex flex-wrap gap-2 mt-2">
            <Link to={healthConsentFormPrintPath(companyId, clientKey)}>
              <Button size="sm" variant="secondary">
                Распечатать бланк
              </Button>
            </Link>
            <Button size="sm" onClick={() => setShowMarkDialog(true)}>
              Бланк подписан, оригинал у нас
            </Button>
          </div>
        </div>
      ) : (
        <div>
          {formOutdated && (
            <p className="text-[11px] text-warning bg-warning-bg rounded-lg px-2 py-1 mb-1.5">
              Бланк подписан по прежней редакции текста — отметка по-прежнему действует.
            </p>
          )}
          {editing ? (
            <div className="flex flex-col gap-2">
              <textarea
                rows={2}
                value={value}
                onChange={(e) => setValue(e.target.value)}
                placeholder="Аллергия на состав, беременность, кожные заболевания…"
                className="w-full rounded-lg border border-[#EAC5B9] px-3 py-2 text-sm outline-none focus:border-[#B5624A] resize-none bg-white text-ink"
              />
              {saveError && <p className="text-xs text-danger">{saveError}</p>}
              <div className="flex gap-2">
                <Button size="sm" loading={saveMut.isPending} onClick={() => saveMut.mutate(value)}>
                  Сохранить
                </Button>
                <Button size="sm" variant="ghost" onClick={() => setEditing(false)}>
                  Отмена
                </Button>
              </div>
            </div>
          ) : data?.value ? (
            <div>
              <p className="text-sm text-[#5A3226] whitespace-pre-wrap">{data.value}</p>
              {data.updatedBy && (
                <p className="text-[11px] text-[#B5624A] mt-1">
                  {data.updatedBy}
                  {data.updatedAt && ` · ${format(parseISO(data.updatedAt), 'd MMM yyyy', { locale: ru })}`}
                </p>
              )}
              <div className="flex gap-2 mt-2">
                <Button
                  size="sm"
                  variant="secondary"
                  onClick={() => {
                    setValue(data.value ?? '')
                    setEditing(true)
                  }}
                >
                  Изменить
                </Button>
                <Button size="sm" variant="ghost" loading={deleteMut.isPending} onClick={() => deleteMut.mutate()}>
                  Очистить
                </Button>
              </div>
            </div>
          ) : (
            <div>
              <p className="text-xs text-[#8A4632]">Поле пусто.</p>
              <Button
                size="sm"
                variant="secondary"
                className="mt-2"
                onClick={() => {
                  setValue('')
                  setEditing(true)
                }}
              >
                Заполнить
              </Button>
            </div>
          )}
          <p className="text-[11px] text-[#B5624A] mt-2">{HEALTH_FORM_NO_PHOTO_NOTICE}</p>
          <button
            type="button"
            onClick={() => setShowRevokeDialog(true)}
            className="text-[11px] text-[#8A4632] underline mt-1.5"
          >
            Снять отметку о согласии
          </button>
        </div>
      )}

      {showMarkDialog && (
        <MarkWrittenConsentDialog
          companyId={companyId}
          clientKey={clientKey}
          currentFormVersion={currentFormVersion}
          onSuccess={() => {
            setShowMarkDialog(false)
            invalidate()
          }}
          onClose={() => setShowMarkDialog(false)}
        />
      )}
      {showRevokeDialog && (
        <RevokeWrittenConsentDialog
          companyId={companyId}
          clientKey={clientKey}
          onSuccess={() => {
            setShowRevokeDialog(false)
            invalidate()
          }}
          onClose={() => setShowRevokeDialog(false)}
        />
      )}
    </div>
  )
}
