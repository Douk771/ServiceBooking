import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import { Link } from 'react-router-dom'
import { consentsApi } from '../api/consents'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { Modal } from '../components/ui/Modal'
import { Icon } from '../components/ui/Icon'
import { getLegalErrorMessage } from '../utils/legalError'
import type { ConsentPurpose, ConsentRevokeEffects } from '../types'

function fmt(iso: string) {
  return format(parseISO(iso), 'd MMM yyyy, HH:mm', { locale: ru })
}

type NumericEffectKey = Exclude<keyof ConsentRevokeEffects, 'profileFieldsCleared'>

const EFFECT_LABELS: { key: NumericEffectKey; label: (n: number) => string }[] = [
  { key: 'photosDeleted', label: (n) => `Будет удалено фотографий: ${n}` },
  { key: 'healthNotesDeleted', label: (n) => `Будет очищено полей «Противопоказания»: ${n}` },
  { key: 'queuedNotificationsCancelled', label: (n) => `Будет отменено уже поставленных в очередь уведомлений: ${n}` },
]

function RevokeModal({
  purpose,
  purposeTitle,
  onClose,
}: {
  purpose: ConsentPurpose
  purposeTitle: string
  onClose: () => void
}) {
  const qc = useQueryClient()
  const [reason, setReason] = useState('')

  const { data: preview, isLoading: previewLoading } = useQuery({
    queryKey: ['consent-revoke-preview', purpose],
    queryFn: () => consentsApi.revokePreview(purpose),
  })

  const mut = useMutation({
    mutationFn: () => consentsApi.revoke('PdnConsent', purpose, reason || undefined),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['profile-consents'] }),
  })

  // §41.3 — the document the person already saw (legal/04-pdn-consent.html, цель 1) promises exactly
  // this: revoking `ProviderDelivery` really does stop the notification queue. The screen has to say
  // it too, both here (before confirming) and in the preview above — silence would be a mismatch
  // between what the product says and what it does.
  const stopsNotifications = purpose === 'ProviderDelivery'

  return (
    <Modal title={`Отозвать согласие: ${purposeTitle}`} onClose={onClose}>
      <div className="flex flex-col gap-4">
        {mut.isSuccess ? (
          <>
            <p className="text-sm text-success flex items-center gap-1.5">
              <Icon name="check" size={14} strokeWidth={2} /> Согласие отозвано
            </p>
            {mut.data.effects.photosDeleted > 0 && <p className="text-sm text-ink-soft">Удалено фотографий: {mut.data.effects.photosDeleted}</p>}
            {mut.data.effects.healthNotesDeleted > 0 && (
              <p className="text-sm text-ink-soft">Очищено полей «Противопоказания»: {mut.data.effects.healthNotesDeleted}</p>
            )}
            {mut.data.effects.queuedNotificationsCancelled > 0 && (
              <p className="text-sm text-ink-soft">Отменено уведомлений в очереди: {mut.data.effects.queuedNotificationsCancelled}</p>
            )}
            <Button className="mt-1" onClick={onClose}>
              Закрыть
            </Button>
          </>
        ) : (
          <>
            {previewLoading ? (
              <div className="h-16 bg-cream-deep rounded-xl animate-pulse" />
            ) : preview ? (
              <div className="rounded-xl bg-cream-deep px-4 py-3 text-sm text-ink-soft flex flex-col gap-1">
                <p className="font-medium text-ink">Что произойдёт, если вы отзовёте это согласие:</p>
                {EFFECT_LABELS.filter((e) => preview[e.key] > 0).map((e) => (
                  <p key={e.key}>{e.label(preview[e.key])}</p>
                ))}
                {EFFECT_LABELS.every((e) => preview[e.key] === 0) && <p>Удалять нечего — данных по этой цели ещё нет.</p>}
                {stopsNotifications && (
                  <p className="text-ink font-medium mt-1">
                    Сообщений о записи в WhatsApp вы больше получать не будете — компании продолжат вести вашу запись,
                    но не смогут вам о ней написать.
                  </p>
                )}
              </div>
            ) : null}

            <div className="rounded-xl border border-line px-4 py-3 text-xs text-muted flex flex-col gap-1">
              <p className="font-medium text-ink-soft">Что отзыв НЕ делает:</p>
              <p>— не удаляет историю визитов, отзывы и расчёты салона;</p>
              <p>— не включает и не снимает отписку от уведомлений (это отдельный переключатель);</p>
              <p>— не удаляет аккаунт;</p>
              <p>— не удаляет строки журнала согласий (они хранятся как доказательство).</p>
            </div>

            <div className="flex flex-col gap-1.5">
              <label className="text-[13px] font-medium text-[#4A4038]">Причина (необязательно)</label>
              <textarea
                rows={2}
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                className="rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep resize-none bg-white text-ink"
              />
            </div>

            {mut.isError && <p className="text-sm text-danger">{getLegalErrorMessage(mut.error)}</p>}

            <div className="flex gap-3 pt-1">
              <Button variant="secondary" className="flex-1" onClick={onClose}>
                Отмена
              </Button>
              <Button variant="danger" className="flex-1" loading={mut.isPending} onClick={() => mut.mutate()}>
                Отозвать согласие
              </Button>
            </div>
          </>
        )}
      </div>
    </Modal>
  )
}

/**
 * "Мои согласия" — API_CONTRACT_CYCLE5.md §41, ARCHITECTURE_CYCLE5.md T5-F3. Reachable both from
 * `/profile` and from `ConsentGate` (§41.3, allow-list), so a person who hasn't accepted a new
 * Material revision of Privacy/TermsClient can still see and revoke what they already consented to.
 */
export function ConsentsPage() {
  const qc = useQueryClient()
  const { data, isLoading, isError } = useQuery({ queryKey: ['profile-consents'], queryFn: consentsApi.get })
  const [revokingPurpose, setRevokingPurpose] = useState<{ key: ConsentPurpose; title: string } | null>(null)
  const [selected, setSelected] = useState<ConsentPurpose[]>([])

  const grantMut = useMutation({
    mutationFn: () => {
      if (!data) throw new Error('no document')
      return consentsApi.grant('PdnConsent', data.document.version, selected)
    },
    onSuccess: () => {
      setSelected([])
      qc.invalidateQueries({ queryKey: ['profile-consents'] })
    },
  })

  if (isLoading) {
    return (
      <div className="max-w-[640px] mx-auto px-8 pt-11 pb-24">
        <div className="h-64 bg-cream-deep rounded-3xl animate-pulse" />
      </div>
    )
  }

  if (isError || !data) {
    return (
      <div className="max-w-[640px] mx-auto px-8 pt-11 pb-24">
        <Card className="p-12 text-center text-muted">
          <Icon name="alert-circle" size={32} strokeWidth={1.4} className="mx-auto mb-3" />
          <p>Не удалось загрузить согласия. Попробуйте обновить страницу.</p>
        </Card>
      </div>
    )
  }

  const activeByPurpose = new Map(data.granted.filter((g) => !g.revokedAt).map((g) => [g.purpose, g]))

  return (
    <div className="max-w-[640px] mx-auto px-8 pt-11 pb-24">
      <h1 className="font-serif text-[30px] font-medium text-ink mb-2">Мои согласия</h1>
      <p className="text-sm text-ink-soft mb-6">
        Согласие на обработку персональных данных — отдельный документ. Каждую цель можно дать или отозвать
        независимо от остальных.{' '}
        <Link to="/pdn-consent" target="_blank" className="text-gold hover:text-gold-dark">
          Читать полный текст
        </Link>
      </p>

      {data.versionOutdated && (
        <div className="mb-5 flex items-start gap-2.5 rounded-2xl bg-info-bg text-info px-4 py-3 text-sm">
          <Icon name="alert-circle" size={16} strokeWidth={1.8} className="shrink-0 mt-0.5" />
          <p>Документ обновился после того, как вы давали согласие. Рекомендуем перечитать актуальную версию.</p>
        </div>
      )}

      <Card className="p-[26px] mb-[18px]">
        <div className="flex flex-col gap-4">
          {data.document.purposes.map((p) => {
            const active = activeByPurpose.get(p.key)
            return (
              <div key={p.key} className="flex items-start justify-between gap-4 border-b border-line last:border-0 pb-4 last:pb-0">
                <div className="min-w-0">
                  <p className="text-sm font-medium text-ink">{p.title}</p>
                  {active ? (
                    <p className="text-xs text-success mt-1">
                      Согласие дано {fmt(active.grantedAt)} · версия {active.version}
                    </p>
                  ) : (
                    <label className="flex items-center gap-2 mt-1.5 cursor-pointer">
                      <input
                        type="checkbox"
                        className="w-4 h-4 rounded accent-gold"
                        checked={selected.includes(p.key)}
                        onChange={(e) =>
                          setSelected((prev) => (e.target.checked ? [...prev, p.key] : prev.filter((k) => k !== p.key)))
                        }
                      />
                      <span className="text-xs text-muted">Согласие не дано</span>
                    </label>
                  )}
                </div>
                {active && (
                  <Button variant="secondary" size="sm" className="shrink-0" onClick={() => setRevokingPurpose({ key: p.key, title: p.title })}>
                    Отозвать
                  </Button>
                )}
              </div>
            )
          })}
        </div>

        {selected.length > 0 && (
          <div className="mt-4 pt-4 border-t border-line">
            {grantMut.isError && <p className="text-sm text-danger mb-2">{getLegalErrorMessage(grantMut.error)}</p>}
            <Button loading={grantMut.isPending} onClick={() => grantMut.mutate()}>
              Дать согласие на отмеченное
            </Button>
          </div>
        )}
      </Card>

      <Card className="p-[26px]">
        <h2 className="text-[15.5px] font-semibold text-ink mb-3">История</h2>
        {data.history.length === 0 ? (
          <p className="text-sm text-muted">Пока пусто.</p>
        ) : (
          <div className="flex flex-col gap-2">
            {data.history.map((h) => (
              <div key={h.id} className="text-xs text-muted flex items-center justify-between gap-3 flex-wrap">
                <span>
                  {h.documentKey}
                  {h.purpose ? ` · ${h.purpose}` : ''} · {h.act} · {h.source}
                </span>
                <span>
                  {fmt(h.grantedAt)} · версия {h.documentVersion}
                  {h.revokedAt ? ` · отозвано ${fmt(h.revokedAt)}` : ''}
                </span>
              </div>
            ))}
          </div>
        )}
      </Card>

      {revokingPurpose && (
        <RevokeModal purpose={revokingPurpose.key} purposeTitle={revokingPurpose.title} onClose={() => setRevokingPurpose(null)} />
      )}
    </div>
  )
}
