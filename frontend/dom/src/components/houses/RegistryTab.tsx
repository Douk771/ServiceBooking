import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { InlineError } from '@/components/ui/InlineError'
import { Input } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { fmtDateTime } from '@/utils/dateFormat'
import { staysHousesApi } from '../../api/staysHouses'
import type { HouseManageDto, HouseObjectKind, StaysConflictDto } from '../../types'
import { DIALOG_CLOSES, OBJECT_KINDS, objectKindLabel, publishProblemText, validateRegistry, type Errors, type RegistryField } from '../../utils/houseForms'
import { getStayErrorMessage, httpStatus, plainBody, readConflict } from '../../utils/stayError'
import { SavedNote, SectionCard } from '../cabinet/formParts'
import { StayNotice } from '../StayNotice'
import { houseKey } from './houseKey'
import { useHouseTab } from './houseContext'

/**
 * `HouseManageDto.registryNotice.text` is PLAIN TEXT by contract (API_CONTRACT_CYCLE37.md §37.28: no markup, paragraphs are `\n`) — the exact
 * words the owner attests to. It is printed as text, never as HTML.
 */
function RegistryNoticeText({ text }: { text: string }) {
  return <p className="whitespace-pre-line text-sm leading-relaxed text-ink-soft">{text}</p>
}

/**
 * Registry and publication (`ManageHouses`, ЮР-2): the kind of the object, the number in the registry (when there is one), and the owner's
 * attestation that is recorded on every publication and on every later change of those fields. A house without a number is published
 * under that attestation — the platform does not check the registry, the owner answers for the data.
 */
export function RegistryTab() {
  const { companyId, house, setHouse, canManage } = useHouseTab()
  const qc = useQueryClient()
  const navigate = useNavigate()

  const [kind, setKind] = useState<HouseObjectKind | ''>(house.registry.objectKind ?? '')
  const [number, setNumber] = useState(house.registry.registryNumber ?? '')
  const [url, setUrl] = useState(house.registry.registryUrl ?? '')
  const [errors, setErrors] = useState<Errors<RegistryField>>({})
  const [formError, setFormError] = useState('')
  const [saved, setSaved] = useState(false)
  const [dialog, setDialog] = useState<'publish' | 'registry' | null>(null)
  const [confirm, setConfirm] = useState<'archive' | 'delete' | null>(null)
  const [actionError, setActionError] = useState('')
  const [tariffLimit, setTariffLimit] = useState(false)

  const touch = () => setSaved(false)
  const attestation = { accepted: true as const, noticeVersion: house.registryNotice.version }

  const afterChange = (h: HouseManageDto) => {
    setHouse(h)
    void qc.invalidateQueries({ queryKey: ['stays-houses', companyId] })
    void qc.invalidateQueries({ queryKey: ['stays-company', companyId] })
  }

  const registry = useMutation({
    mutationFn: (withAttestation: boolean) =>
      staysHousesApi.updateRegistry(companyId, house.id, {
        objectKind: kind as HouseObjectKind,
        registryNumber: number.trim() || null,
        registryUrl: url.trim() || null,
        attestation: withAttestation ? attestation : null,
      }),
    onSuccess: (h) => {
      afterChange(h)
      setDialog(null)
      setSaved(true)
      setFormError('')
      setErrors({})
    },
    onError: (err) => {
      const c = readConflict<StaysConflictDto>(err)
      if (c?.code === 'AttestationRequired') {
        setDialog('registry')
        return
      }
      setDialog(null)
      const text = plainBody(err)
      if (httpStatus(err) === 400 && /реестр/i.test(text)) return setErrors({ [/Ссылка/.test(text) ? 'registryUrl' : 'registryNumber']: text })
      setFormError(c?.message ?? getStayErrorMessage(err, 'Не удалось сохранить сведения.'))
    },
  })

  const publish = useMutation({
    mutationFn: () => staysHousesApi.publish(companyId, house.id, { attestation }),
    onSuccess: (h) => {
      afterChange(h)
      setDialog(null)
      setActionError('')
      setTariffLimit(false)
    },
    onError: (err) => {
      setTariffLimit(httpStatus(err) === 402)
      setActionError(readConflict<StaysConflictDto>(err)?.message ?? getStayErrorMessage(err, 'Не удалось опубликовать дом.'))
      setDialog(null)
    },
  })
  const unpublish = useMutation({
    mutationFn: () => staysHousesApi.unpublish(companyId, house.id),
    onSuccess: (h) => {
      afterChange(h)
      setActionError('')
    },
    onError: (err) => setActionError(getStayErrorMessage(err, 'Не удалось снять дом с публикации.')),
  })
  const archive = useMutation({
    mutationFn: () => staysHousesApi.archive(companyId, house.id),
    onSuccess: (h) => {
      afterChange(h)
      setConfirm(null)
      setActionError('')
    },
    onError: (err) => {
      setConfirm(null)
      setActionError(getStayErrorMessage(err, 'Не удалось архивировать дом.'))
    },
  })
  const remove = useMutation({
    mutationFn: () => staysHousesApi.remove(companyId, house.id),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['stays-houses', companyId] })
      void qc.removeQueries({ queryKey: houseKey(companyId, house.id) })
      navigate(`/cabinet/${companyId}/houses`, { replace: true })
    },
    onError: (err) => {
      setConfirm(null)
      setActionError(readConflict<StaysConflictDto>(err)?.message ?? getStayErrorMessage(err, 'Не удалось удалить дом.'))
    },
  })

  if (!canManage) return <p className="text-sm text-ink-soft">Реестр и публикацию дома ведёт владелец.</p>

  const blockers = house.publishProblems.filter((c) => !DIALOG_CLOSES.includes(c))
  const submitRegistry = () => {
    const v = validateRegistry({ objectKind: kind, registryNumber: number, registryUrl: url })
    setErrors(v)
    setFormError('')
    if (Object.keys(v).length > 0) return
    // A published house: a change of these fields needs the owner's new attestation in the same request.
    if (house.isPublished) setDialog('registry')
    else registry.mutate(false)
  }

  const last = house.registry.lastAttestation
  return (
    <div className="flex flex-col gap-6">
      <SectionCard title="Вид объекта и реестр">
        <form
          noValidate
          className="flex flex-col gap-5"
          onSubmit={(e) => {
            e.preventDefault()
            submitRegistry()
          }}
        >
          <fieldset className="flex flex-col gap-2.5">
            <legend className="mb-1 text-[13px] font-medium text-[#4A4038]">Вид объекта *</legend>
            {OBJECT_KINDS.map((o) => (
              <label key={o.value} className={`flex min-h-[44px] cursor-pointer items-start gap-3 rounded-2xl border p-4 ${kind === o.value ? 'border-ink bg-cream-deep/50' : 'border-line hover:border-line-strong'}`}>
                <input type="radio" name="object-kind" className="mt-1 h-5 w-5 accent-gold" checked={kind === o.value} onChange={() => { setKind(o.value); touch() }} />
                <span>
                  <span className="block text-sm font-semibold text-ink">{o.label}</span>
                  <span className="mt-0.5 block text-xs text-ink-soft">{o.hint}</span>
                </span>
              </label>
            ))}
            {errors.objectKind && <p className="text-xs text-danger">{errors.objectKind}</p>}
          </fieldset>
          <Input label="Номер в реестре" placeholder="если есть" maxLength={32} value={number} error={errors.registryNumber} onChange={(e) => { setNumber(e.target.value); touch() }} />
          <Input label="Ссылка на запись в реестре" placeholder="https://..." maxLength={500} value={url} error={errors.registryUrl} onChange={(e) => { setUrl(e.target.value); touch() }} />
          <StayNotice textKey="StayRegistryOwnerNotice" />
          {formError && <InlineError>{formError}</InlineError>}
          <div className="flex items-center gap-3">
            <Button type="submit" loading={registry.isPending} className="min-h-[44px]">
              Сохранить
            </Button>
            <SavedNote show={saved} />
          </div>
          {house.isPublished && <p className="text-xs text-muted">Дом опубликован: изменение этих сведений потребует нового подтверждения.</p>}
          {last && (
            <p className="text-xs text-muted" data-testid="last-attestation">
              Последнее подтверждение: {last.attestedByName}, {fmtDateTime(last.attestedAtUtc)} — {objectKindLabel(last.objectKind)}
              {last.registryNumber ? `, № ${last.registryNumber}` : ', без номера'}.
            </p>
          )}
        </form>
      </SectionCard>

      <SectionCard title="Публикация" description="Гости видят и бронируют только опубликованные дома.">
        <p className="text-sm">
          Сейчас:{' '}
          <span className="font-semibold text-ink">{house.isArchived ? 'в архиве' : house.isPublished ? 'опубликован' : 'не опубликован'}</span>
        </p>
        {!house.isPublished && !house.isArchived && blockers.length > 0 && (
          <ul className="rounded-2xl bg-warning-bg px-4 py-3 text-sm text-warning" data-testid="publish-blockers">
            {blockers.map((c) => (
              <li key={c}>· {publishProblemText(c)}</li>
            ))}
          </ul>
        )}
        {actionError && (
          <div>
            <InlineError>{actionError}</InlineError>
            {tariffLimit && (
              <p className="mt-2 text-sm">
                <Link to="/cabinet/subscription" className="font-semibold text-gold-dark underline">
                  Выбрать тариф
                </Link>
              </p>
            )}
          </div>
        )}
        <div className="flex flex-wrap gap-3">
          {!house.isArchived && !house.isPublished && (
            <Button className="min-h-[44px]" disabled={blockers.length > 0} onClick={() => { setActionError(''); setDialog('publish') }}>
              Опубликовать
            </Button>
          )}
          {house.isPublished && (
            <Button variant="secondary" className="min-h-[44px]" loading={unpublish.isPending} onClick={() => unpublish.mutate()}>
              Снять с публикации
            </Button>
          )}
          {!house.isArchived && (
            <Button variant="secondary" className="min-h-[44px]" onClick={() => setConfirm('archive')}>
              В архив
            </Button>
          )}
          <Button variant="danger" className="min-h-[44px]" onClick={() => setConfirm('delete')}>
            Удалить дом
          </Button>
        </div>
      </SectionCard>

      {(dialog === 'publish' || dialog === 'registry') && (
        <AttestationDialog
          text={house.registryNotice.text}
          title={dialog === 'publish' ? 'Опубликовать дом' : 'Подтвердите изменение сведений'}
          cta={dialog === 'publish' ? 'Подтверждаю и публикую' : 'Подтверждаю и сохраняю'}
          pending={dialog === 'publish' ? publish.isPending : registry.isPending}
          summary={`${kind ? objectKindLabel(kind) : '—'}${number.trim() ? `, № ${number.trim()}` : ', без номера'}`}
          onConfirm={() => (dialog === 'publish' ? publish.mutate() : registry.mutate(true))}
          onClose={() => setDialog(null)}
        />
      )}
      {confirm && (
        <Modal title={confirm === 'archive' ? 'Отправить дом в архив?' : 'Удалить дом?'} onClose={() => setConfirm(null)} dismissible={!archive.isPending && !remove.isPending}>
          <p className="text-sm text-ink-soft">
            {confirm === 'archive'
              ? 'Дом снимется с публикации и пропадёт из каталога. Уже оформленные брони продолжат действовать.'
              : 'Дом удалится вместе с фото и ценами. Если у дома были брони, удалить его нельзя — только в архив.'}
          </p>
          <div className="mt-5 flex gap-3">
            <Button variant="secondary" className="min-h-[44px] flex-1" onClick={() => setConfirm(null)}>
              Отмена
            </Button>
            <Button variant="danger" className="min-h-[44px] flex-1" loading={archive.isPending || remove.isPending} onClick={() => (confirm === 'archive' ? archive.mutate() : remove.mutate())}>
              {confirm === 'archive' ? 'В архив' : 'Удалить'}
            </Button>
          </div>
        </Modal>
      )}
    </div>
  )
}

function AttestationDialog({
  text,
  title,
  cta,
  summary,
  pending,
  onConfirm,
  onClose,
}: {
  text: string
  title: string
  cta: string
  summary: string
  pending: boolean
  onConfirm: () => void
  onClose: () => void
}) {
  const [accepted, setAccepted] = useState(false)
  return (
    <Modal title={title} onClose={onClose} dismissible={!pending}>
      <div className="flex flex-col gap-4">
        <RegistryNoticeText text={text} />
        <p className="rounded-xl bg-cream-deep px-4 py-2.5 text-sm text-ink">
          Вид объекта и номер: <span className="font-semibold">{summary}</span>
        </p>
        <label className="flex min-h-[44px] cursor-pointer items-start gap-3 text-sm text-ink">
          <input type="checkbox" className="mt-0.5 h-5 w-5 shrink-0 accent-gold" checked={accepted} onChange={(e) => setAccepted(e.target.checked)} />
          <span>Подтверждаю, что сведения о доме достоверны, и отвечаю за них. Дата, автор и текст подтверждения сохраняются.</span>
        </label>
        <div className="flex gap-3">
          <Button variant="secondary" className="min-h-[44px] flex-1" onClick={onClose} disabled={pending}>
            Отмена
          </Button>
          <Button className="min-h-[44px] flex-1" disabled={!accepted} loading={pending} onClick={onConfirm}>
            {cta}
          </Button>
        </div>
      </div>
    </Modal>
  )
}
