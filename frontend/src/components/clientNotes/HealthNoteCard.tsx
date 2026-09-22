import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AxiosError } from 'axios'
import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import { clientConsentsApi } from '../../api/clientConsents'
import { Button } from '../ui/Button'
import { Icon } from '../ui/Icon'
import { ClientConsentModal } from './ClientConsentModal'

interface Props {
  companyId: string
  clientKey: string
}

function is400WithRequiredConsent(err: unknown): boolean {
  const ax = err as AxiosError
  const data = ax?.response?.data
  return ax?.response?.status === 400 && !!data && typeof data === 'object' && 'requiredTextKey' in data
}

/**
 * "Противопоказания и особенности здоровья" — API_CONTRACT_CYCLE5.md §45; ARCHITECTURE_CYCLE5.md
 * T5-F5, US-77. Visually separate from the ordinary note (own card, own colour), never mixed into it.
 * `MasterClientsPage` doesn't render this component at all for SuperAdmin — the requirement is that
 * the interface show no block, not an empty/403 one (§45.1, checklist item in §53).
 */
export function HealthNoteCard({ companyId, clientKey }: Props) {
  const qc = useQueryClient()
  const [editing, setEditing] = useState(false)
  const [value, setValue] = useState('')
  const [showConsent, setShowConsent] = useState(false)
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
      // §45.2 — the one 400 in this cycle with a JSON body: `{requiredTextKey: "HealthDataConsent"}`.
      // Route straight to the consent form instead of showing a bare error the owner can't act on.
      if (is400WithRequiredConsent(err)) {
        setShowConsent(true)
      } else {
        const ax = err as AxiosError
        setSaveError(typeof ax?.response?.data === 'string' ? ax.response.data : 'Не удалось сохранить. Попробуйте снова.')
      }
    },
  })

  const consentMut = useMutation({
    mutationFn: (textVersion: string) => clientConsentsApi.confirmHealthConsent(companyId, clientKey, textVersion),
    onSuccess: () => {
      setShowConsent(false)
      saveMut.mutate(value)
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

  return (
    <div className="rounded-xl border border-[#EAC5B9] bg-[#FBF3EF] px-3.5 py-3">
      <div className="flex items-center gap-1.5 mb-1.5">
        <Icon name="alert-circle" size={13} strokeWidth={1.8} className="text-[#B5624A]" />
        <p className="text-xs font-semibold text-[#8A4632] uppercase tracking-wide">Противопоказания и особенности здоровья</p>
      </div>

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
          <p className="text-xs text-[#8A4632]">Поле пусто. Заполняется только с согласия клиента.</p>
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

      {showConsent && (
        <ClientConsentModal
          textKey="HealthDataConsent"
          title="Согласие на обработку сведений о здоровье"
          loading={consentMut.isPending}
          error={consentMut.isError ? 'Не удалось записать согласие. Попробуйте снова.' : undefined}
          onConfirm={(textVersion) => consentMut.mutate(textVersion)}
          onClose={() => setShowConsent(false)}
        />
      )}
    </div>
  )
}
