import { useRef, useState } from 'react'
import { useParams, Link } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useForm, Controller } from 'react-hook-form'
import { companiesApi } from '../../api/companies'
import { servicesApi } from '../../api/services'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { Input } from '../../components/ui/Input'
import { Modal } from '../../components/ui/Modal'
import { Icon } from '../../components/ui/Icon'
import { Avatar } from '../../components/ui/Avatar'
import { CityCombobox } from '../../components/ui/CityCombobox'
import { ScheduleTab } from './ScheduleTab'
import { CompanyPhotosSection } from './CompanyPhotosSection'
import { AddressVerifyField } from '../../components/company/AddressVerifyField'
import { NotificationSettingsTab } from './NotificationSettingsTab'
import { NotificationTemplatesTab } from './NotificationTemplatesTab'
import { NotificationLogTab } from './NotificationLogTab'
import { getAddMemberErrorMessage } from '../../utils/memberError'
import { getCompanyManageErrorMessage, getLogoErrorMessage } from '../../utils/companyManageError'
import { getProvidesServicesErrorMessage } from '../../utils/providesServicesError'
import { parseBookingHorizonInput } from '../../utils/bookingHorizon'
import { getUploadErrorMessage } from '../../utils/uploadError'
import { PhoneInput } from '../../components/ui/PhoneInput'
import { formatPhone, isRussianPhone } from '../../utils/phone'
import { formatCityTimeZone } from '../../utils/timezone'
import type { Service, City } from '../../types'

// ── Services tab ──────────────────────────────────────────────────────────────

interface ServiceFormData {
  name: string
  description: string
  durationMinutes: number
  price: number
}

function ServicesTab({ companyId }: { companyId: string }) {
  const qc = useQueryClient()
  const [editing, setEditing] = useState<Service | null>(null)
  const [showAdd, setShowAdd] = useState(false)
  const [formError, setFormError] = useState('')
  const [deleteError, setDeleteError] = useState('')
  const [imageError, setImageError] = useState('')
  const imageInputRef = useRef<HTMLInputElement>(null)

  const { data: services, isLoading } = useQuery({
    queryKey: ['services', companyId],
    queryFn: () => servicesApi.getByCompany(companyId),
  })

  const {
    register,
    handleSubmit,
    reset,
    setValue,
    formState: { errors },
  } = useForm<ServiceFormData>()

  const openEdit = (s: Service) => {
    setEditing(s)
    setImageError('')
    setValue('name', s.name)
    setValue('description', s.description ?? '')
    setValue('durationMinutes', s.durationMinutes)
    setValue('price', s.price)
  }

  const closeForm = () => {
    setEditing(null)
    setShowAdd(false)
    setFormError('')
    setImageError('')
    reset()
  }

  // Only available once the service already has an id — a brand-new service must be saved first
  // (US-25: POST /api/services/{id}/image needs an existing service).
  const imageMut = useMutation({
    mutationFn: (file: File) => servicesApi.uploadImage(editing!.id, file),
    onMutate: () => setImageError(''),
    onSuccess: (updated) => {
      // Refreshes the thumbnail inside the still-open modal immediately, not just the list behind it —
      // `editing` is a snapshot taken when the modal opened, so without this the new photo only shows
      // up after closing and reopening the form.
      setEditing(updated)
      qc.invalidateQueries({ queryKey: ['services', companyId] })
    },
    onError: (err: unknown) => setImageError(getUploadErrorMessage(err)),
  })

  const createMut = useMutation({
    mutationFn: (d: ServiceFormData) =>
      servicesApi.create({
        companyId,
        name: d.name,
        description: d.description || undefined,
        durationMinutes: +d.durationMinutes,
        price: +d.price,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['services', companyId] })
      closeForm()
    },
    onError: (err: unknown) =>
      setFormError(getCompanyManageErrorMessage(err, 'Не удалось сохранить услугу. Попробуйте снова.')),
  })

  const updateMut = useMutation({
    mutationFn: (d: ServiceFormData) =>
      servicesApi.update(editing!.id, {
        companyId,
        name: d.name,
        description: d.description || undefined,
        durationMinutes: +d.durationMinutes,
        price: +d.price,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['services', companyId] })
      closeForm()
    },
    onError: (err: unknown) =>
      setFormError(getCompanyManageErrorMessage(err, 'Не удалось сохранить услугу. Попробуйте снова.')),
  })

  const deleteMut = useMutation({
    mutationFn: (id: string) => servicesApi.delete(id),
    onSuccess: () => {
      setDeleteError('')
      qc.invalidateQueries({ queryKey: ['services', companyId] })
    },
    onError: (err: unknown) =>
      setDeleteError(getCompanyManageErrorMessage(err, 'Не удалось удалить услугу. Попробуйте снова.')),
  })

  const onSubmit = (d: ServiceFormData) => (editing ? updateMut.mutate(d) : createMut.mutate(d))
  const isPending = createMut.isPending || updateMut.isPending

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <h2 className="text-lg font-semibold text-ink">Услуги</h2>
        <Button size="sm" onClick={() => setShowAdd(true)}>
          <Icon name="plus" size={14} strokeWidth={2} /> Добавить
        </Button>
      </div>

      {isLoading ? (
        <div className="grid gap-3">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="h-16 bg-cream-deep rounded-2xl animate-pulse" />
          ))}
        </div>
      ) : services && services.length > 0 ? (
        <div className="grid gap-3">
          {services.map((s) => (
            <Card key={s.id} className="p-4 flex items-center justify-between gap-4">
              <div className="flex items-center gap-3 min-w-0">
                {s.imageUrl ? (
                  <img src={s.imageUrl} alt={s.name} className="w-11 h-11 rounded-xl object-cover shrink-0" />
                ) : (
                  <div className="w-11 h-11 rounded-xl bg-cream-deep flex items-center justify-center text-gold-dark font-bold shrink-0">
                    {s.name[0]?.toUpperCase() ?? '?'}
                  </div>
                )}
                <div className="min-w-0">
                  <p className="font-medium text-ink">{s.name}</p>
                  <p className="text-sm text-muted flex items-center gap-1">
                    <Icon name="clock" size={13} strokeWidth={1.7} /> {s.durationMinutes} мин ·{' '}
                    {s.price.toLocaleString('ru-RU')} ₽
                  </p>
                  {s.description && <p className="text-xs text-muted mt-0.5 line-clamp-1">{s.description}</p>}
                </div>
              </div>
              <div className="flex gap-2 shrink-0">
                <Button variant="secondary" size="sm" onClick={() => openEdit(s)}>
                  Изменить
                </Button>
                <Button variant="danger" size="sm" loading={deleteMut.isPending} onClick={() => deleteMut.mutate(s.id)}>
                  Удалить
                </Button>
              </div>
            </Card>
          ))}
        </div>
      ) : (
        <Card className="p-12 text-center text-muted">
          <Icon name="settings" size={32} strokeWidth={1.4} className="mx-auto mb-2" />
          <p>Услуги ещё не добавлены</p>
        </Card>
      )}

      {deleteError && <p className="text-sm text-danger mt-3">{deleteError}</p>}

      {(showAdd || editing) && (
        <Modal title={editing ? 'Редактировать услугу' : 'Добавить услугу'} onClose={closeForm}>
          <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
            {editing && (
              <div className="flex items-center gap-3">
                {editing.imageUrl ? (
                  <img
                    src={editing.imageUrl}
                    alt={editing.name}
                    className="w-14 h-14 rounded-xl object-cover shrink-0"
                  />
                ) : (
                  <div className="w-14 h-14 rounded-xl bg-cream-deep flex items-center justify-center text-gold-dark font-bold text-lg shrink-0">
                    {editing.name[0]?.toUpperCase() ?? '?'}
                  </div>
                )}
                <div>
                  <input
                    ref={imageInputRef}
                    type="file"
                    accept="image/jpeg,image/png,image/webp"
                    className="hidden"
                    onChange={(e) => {
                      const f = e.target.files?.[0]
                      if (f) imageMut.mutate(f)
                      e.target.value = ''
                    }}
                  />
                  <Button
                    type="button"
                    size="sm"
                    variant="secondary"
                    loading={imageMut.isPending}
                    onClick={() => imageInputRef.current?.click()}
                  >
                    {editing.imageUrl ? 'Заменить фото' : 'Загрузить фото'}
                  </Button>
                  <p className="text-xs text-muted mt-1">JPEG, PNG или WEBP, до 5 МБ</p>
                  {imageError && <p className="text-xs text-danger mt-1">{imageError}</p>}
                </div>
              </div>
            )}
            {!editing && <p className="text-xs text-muted -mt-1">Фото услуги можно будет добавить после сохранения.</p>}
            <Input
              label="Название *"
              placeholder="Название услуги..."
              error={errors.name?.message}
              {...register('name', { required: 'Введите название' })}
            />
            <div className="flex flex-col gap-1">
              <label className="text-sm font-medium text-ink-soft">Описание</label>
              <textarea
                className="rounded-xl border border-line px-3 py-2 text-sm outline-none focus:border-gold focus:ring-2 focus:ring-cream-deep resize-none"
                rows={2}
                placeholder="Описание услуги..."
                {...register('description')}
              />
            </div>
            <div className="grid grid-cols-2 gap-3">
              <Input
                label="Длительность (мин) *"
                type="number"
                min={5}
                step={5}
                placeholder="60"
                error={errors.durationMinutes?.message}
                {...register('durationMinutes', {
                  required: 'Введите длительность',
                  min: { value: 5, message: 'Минимум 5 мин' },
                })}
              />
              <Input
                label="Цена (₽) *"
                type="number"
                min={0}
                step={50}
                placeholder="1500"
                error={errors.price?.message}
                {...register('price', {
                  required: 'Введите цену',
                  min: { value: 0, message: 'Цена не может быть отрицательной' },
                })}
              />
            </div>
            {formError && <p className="text-sm text-danger">{formError}</p>}
            <div className="flex gap-3 pt-2">
              <Button type="button" variant="secondary" className="flex-1" onClick={closeForm}>
                Отмена
              </Button>
              <Button type="submit" className="flex-1" loading={isPending}>
                {editing ? 'Сохранить' : 'Добавить'}
              </Button>
            </div>
          </form>
        </Modal>
      )}
    </div>
  )
}

// ── Members tab ───────────────────────────────────────────────────────────────

const roleLabel: Record<string, string> = { Master: 'Мастер', CompanyOwner: 'Владелец' }

interface MemberCardProps {
  member: import('../../api/companies').MemberDto
  companyId: string
  services: Service[]
  onRemove: (id: string) => void
  removeLoading: boolean
}

function MemberCard({ member: m, companyId, services, onRemove, removeLoading }: MemberCardProps) {
  const qc = useQueryClient()
  const [expanded, setExpanded] = useState(false)
  const [selected, setSelected] = useState<Set<string>>(new Set(m.serviceIds))
  const [dirty, setDirty] = useState(false)
  const [commission, setCommission] = useState(m.commissionPercent)
  const [commissionDirty, setCommissionDirty] = useState(false)
  const [saveError, setSaveError] = useState('')
  const [commissionError, setCommissionError] = useState('')
  const [providesServices, setProvidesServices] = useState(m.providesServices)
  const [providesError, setProvidesError] = useState('')
  const [pendingConfirm, setPendingConfirm] = useState(false)

  const providesMut = useMutation({
    mutationFn: (vars: { value: boolean; confirm: boolean }) =>
      companiesApi.updateMemberProvidesServices(companyId, m.id, vars.value, vars.confirm),
    onSuccess: (_res, vars) => {
      setProvidesError('')
      setPendingConfirm(false)
      setProvidesServices(vars.value)
      qc.invalidateQueries({ queryKey: ['company-members', companyId] })
    },
    onError: (err: unknown) => {
      const { message, needsConfirmation } = getProvidesServicesErrorMessage(err)
      setProvidesError(message)
      setPendingConfirm(needsConfirmation)
    },
  })

  const toggleProvidesServices = () => {
    const next = !providesServices
    setProvidesError('')
    setPendingConfirm(false)
    providesMut.mutate({ value: next, confirm: false })
  }

  const toggle = (id: string) => {
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
    setDirty(true)
  }

  const saveMut = useMutation({
    mutationFn: () => companiesApi.updateMemberServices(companyId, m.id, [...selected]),
    onSuccess: () => {
      setSaveError('')
      setDirty(false)
      qc.invalidateQueries({ queryKey: ['company-members', companyId] })
    },
    onError: (err: unknown) =>
      setSaveError(getCompanyManageErrorMessage(err, 'Не удалось сохранить услуги сотрудника.')),
  })

  const commissionMut = useMutation({
    mutationFn: () => companiesApi.updateMemberCommission(companyId, m.id, commission),
    onSuccess: () => {
      setCommissionError('')
      setCommissionDirty(false)
      qc.invalidateQueries({ queryKey: ['company-members', companyId] })
    },
    onError: (err: unknown) => setCommissionError(getCompanyManageErrorMessage(err, 'Не удалось сохранить комиссию.')),
  })

  return (
    <Card className="overflow-hidden">
      <div className="p-4 flex items-center justify-between gap-4">
        <div className="flex items-center gap-3 min-w-0">
          <Avatar avatarUrl={m.avatarUrl} firstName={m.firstName} lastName={m.lastName} size={40} className="text-sm" />
          <div className="min-w-0">
            <p className="font-medium text-ink">
              {m.firstName} {m.lastName}
            </p>
            <p className="text-sm text-muted truncate">
              {(m.phone ? formatPhone(m.phone) : '') || m.email} · {roleLabel[m.role] ?? m.role}
            </p>
            {m.bio && <p className="text-xs text-muted mt-0.5 truncate">{m.bio}</p>}
            {commissionError && <p className="text-xs text-danger mt-0.5">{commissionError}</p>}
          </div>
        </div>
        <div className="flex items-center gap-2 shrink-0">
          <label className="flex items-center gap-1.5 cursor-pointer" title="Показывать этого сотрудника клиенту при онлайн-записи">
            <input
              type="checkbox"
              className="w-4 h-4 rounded accent-gold"
              checked={providesServices}
              disabled={providesMut.isPending}
              onChange={toggleProvidesServices}
            />
            <span className="text-xs text-muted whitespace-nowrap">Оказывает услуги</span>
          </label>
          <div className="flex items-center gap-1.5">
            <label className="text-xs text-muted whitespace-nowrap">Комиссия:</label>
            <input
              type="number"
              min={0}
              max={100}
              step={1}
              value={commission}
              onChange={(e) => {
                setCommission(Number(e.target.value))
                setCommissionDirty(true)
              }}
              className="w-16 rounded-lg border border-line px-2 py-1 text-sm outline-none focus:border-gold"
            />
            <span className="text-xs text-muted">%</span>
            {commissionDirty && (
              <Button size="sm" loading={commissionMut.isPending} onClick={() => commissionMut.mutate()}>
                <Icon name="check" size={13} strokeWidth={2} />
              </Button>
            )}
          </div>
          {services.length > 0 && (
            <button
              onClick={() => setExpanded((e) => !e)}
              className="flex items-center gap-1 text-xs text-muted hover:text-gold-dark border border-line hover:border-line-strong rounded-lg px-2 py-1 transition-colors"
            >
              Услуги {selected.size > 0 ? `(${selected.size})` : ''}
              <Icon
                name="chevron-down"
                size={12}
                strokeWidth={1.8}
                className={`transition-transform ${expanded ? 'rotate-180' : ''}`}
              />
            </button>
          )}
          <Button variant="danger" size="sm" loading={removeLoading} onClick={() => onRemove(m.id)}>
            Удалить
          </Button>
        </div>
      </div>

      {providesError && (
        <div className="mx-4 mb-3 rounded-xl bg-warning-bg text-warning text-xs px-3 py-2.5 flex flex-col gap-2">
          <span>{providesError}</span>
          {pendingConfirm && (
            <div className="flex gap-2">
              <Button size="sm" variant="danger" loading={providesMut.isPending} onClick={() => providesMut.mutate({ value: false, confirm: true })}>
                Всё равно выключить
              </Button>
              <Button
                size="sm"
                variant="secondary"
                onClick={() => {
                  setProvidesError('')
                  setPendingConfirm(false)
                }}
              >
                Отмена
              </Button>
            </div>
          )}
        </div>
      )}

      {expanded && (
        <div className="border-t border-line px-4 py-3 bg-cream-deep">
          <p className="text-xs font-medium text-muted uppercase tracking-wide mb-2">Услуги сотрудника</p>
          <div className="flex flex-wrap gap-2 mb-3">
            {services.map((s) => {
              const checked = selected.has(s.id)
              return (
                <label
                  key={s.id}
                  className={`flex items-center gap-1.5 px-3 py-1.5 rounded-xl border text-sm cursor-pointer select-none transition-all ${
                    checked
                      ? 'bg-cream-deep border-line-strong text-gold-dark'
                      : 'bg-white border-line text-ink-soft hover:border-line-strong'
                  }`}
                >
                  <input type="checkbox" className="sr-only" checked={checked} onChange={() => toggle(s.id)} />
                  <span
                    className={`w-3.5 h-3.5 rounded border flex items-center justify-center shrink-0 ${checked ? 'bg-gold-dark border-gold-dark' : 'border-line-strong'}`}
                  >
                    {checked && <Icon name="check" size={9} strokeWidth={2.5} className="text-cream" />}
                  </span>
                  {s.name}
                </label>
              )
            })}
          </div>
          <div className="flex items-center gap-3">
            {dirty && (
              <Button size="sm" loading={saveMut.isPending} onClick={() => saveMut.mutate()}>
                Сохранить
              </Button>
            )}
            {saveMut.isSuccess && !dirty && (
              <span className="text-xs text-success font-medium flex items-center gap-1">
                <Icon name="check" size={12} strokeWidth={2} /> Сохранено
              </span>
            )}
            {saveError && <span className="text-xs text-danger">{saveError}</span>}
          </div>
        </div>
      )}
    </Card>
  )
}

export function MembersTab({ companyId }: { companyId: string }) {
  const qc = useQueryClient()
  const [showAdd, setShowAdd] = useState(false)
  const [removeError, setRemoveError] = useState('')
  const {
    register,
    handleSubmit,
    reset,
    control,
    formState: { errors },
  } = useForm<{
    phone: string
    firstName: string
    lastName: string
    role: string
    bio: string
    email: string
  }>({
    defaultValues: { role: 'Master', phone: '' },
  })

  const { data: members, isLoading } = useQuery({
    queryKey: ['company-members', companyId],
    queryFn: () => companiesApi.getMembers(companyId),
  })

  const { data: services } = useQuery({
    queryKey: ['services', companyId],
    queryFn: () => servicesApi.getByCompany(companyId),
  })

  // Same query as SettingsTab — React Query dedupes by key, so this doesn't add a request.
  const { data: companies } = useQuery({ queryKey: ['my-companies'], queryFn: companiesApi.getMy })
  const company = companies?.find((c) => c.id === companyId)

  // Cycle 7 (ARCHITECTURE_CYCLE7.md §56): maxEmployees is now the AGGREGATE seat cap across the
  // whole billing account (all of the owner's companies), not a per-company limit. Comparing it
  // to THIS company's members.length would under-count staff at the account's other companies and
  // gate "add employee" too early. The server computes canAddEmployee with the same rule the 402
  // uses, so that (not client arithmetic) is the only source of truth for gating the button.
  const atMemberLimit = company?.canAddEmployee === false

  const addMut = useMutation({
    mutationFn: (d: { phone: string; firstName: string; lastName: string; role: string; bio: string; email: string }) =>
      companiesApi.addMember(
        companyId,
        d.phone,
        d.firstName,
        d.lastName,
        d.role,
        d.bio || undefined,
        d.email || undefined,
      ),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['company-members', companyId] })
      setShowAdd(false)
      reset()
    },
  })

  const removeMut = useMutation({
    mutationFn: (memberId: string) => companiesApi.removeMember(companyId, memberId),
    onSuccess: () => {
      setRemoveError('')
      qc.invalidateQueries({ queryKey: ['company-members', companyId] })
    },
    onError: (err: unknown) => setRemoveError(getCompanyManageErrorMessage(err, 'Не удалось удалить сотрудника.')),
  })

  return (
    <div>
      <div className="flex items-start justify-between mb-4">
        <h2 className="text-lg font-semibold text-ink">Сотрудники</h2>
        <div className="flex flex-col items-end gap-1">
          <Button size="sm" onClick={() => setShowAdd(true)} disabled={atMemberLimit}>
            <Icon name="plus" size={14} strokeWidth={2} /> Добавить
          </Button>
          {company?.accountSeatsLimit != null && (
            <p className="text-xs text-muted">
              {company.accountSeatsUsed ?? 0} / {company.accountSeatsLimit} сотрудников суммарно по подписке
            </p>
          )}
        </div>
      </div>
      {atMemberLimit && (
        <p className="text-xs text-warning mb-4 -mt-2">
          Достигнут суммарный лимит сотрудников по подписке — повысьте тариф, чтобы добавить ещё
        </p>
      )}
      {removeError && <p className="text-sm text-danger mb-4 -mt-2">{removeError}</p>}

      {isLoading ? (
        <div className="grid gap-3">
          {Array.from({ length: 2 }).map((_, i) => (
            <div key={i} className="h-16 bg-cream-deep rounded-2xl animate-pulse" />
          ))}
        </div>
      ) : members && members.length > 0 ? (
        <div className="grid gap-3">
          {members.map((m) => (
            <MemberCard
              key={m.id}
              member={m}
              companyId={companyId}
              services={services ?? []}
              onRemove={(id) => removeMut.mutate(id)}
              removeLoading={removeMut.isPending}
            />
          ))}
        </div>
      ) : (
        <Card className="p-12 text-center text-muted">
          <Icon name="users" size={32} strokeWidth={1.4} className="mx-auto mb-2" />
          <p>Сотрудники ещё не добавлены</p>
        </Card>
      )}

      {showAdd && (
        <Modal
          title="Добавить сотрудника"
          onClose={() => {
            setShowAdd(false)
            reset()
          }}
        >
          <form onSubmit={handleSubmit((d) => addMut.mutate(d))} className="flex flex-col gap-4">
            <p className="text-xs text-muted bg-warning-bg border border-[#EAD9AC] rounded-xl px-3 py-2">
              Если пользователь ещё не зарегистрирован — аккаунт будет создан автоматически.
              <br />
              Временный пароль: <strong>Sb + последние 6 цифр телефона</strong> (например, <em>+7 999 123‑45‑67</em> →{' '}
              <em>Sb234567</em>)
            </p>
            <Controller
              name="phone"
              control={control}
              rules={{
                required: 'Введите телефон',
                validate: (v) =>
                  isRussianPhone(v) || 'Пока принимаем только российские номера, в формате +7 (900) 000-00-00',
              }}
              render={({ field }) => (
                <PhoneInput label="Телефон *" error={errors.phone?.message} value={field.value} onChange={field.onChange} />
              )}
            />
            <div className="grid grid-cols-2 gap-3">
              <Input label="Имя *" placeholder="Иван" {...register('firstName', { required: true })} />
              <Input label="Фамилия *" placeholder="Иванов" {...register('lastName', { required: true })} />
            </div>
            <div className="flex flex-col gap-1">
              <label className="text-sm font-medium text-ink-soft">Роль</label>
              <select
                className="rounded-xl border border-line px-3 py-2 text-sm outline-none focus:border-gold focus:ring-2 focus:ring-cream-deep"
                {...register('role')}
              >
                <option value="Master">Мастер</option>
                <option value="CompanyOwner">Совладелец</option>
              </select>
            </div>
            <Input label="Email (необязательно)" type="email" placeholder="master@example.com" {...register('email')} />
            <Input label="О сотруднике" placeholder="Специализация, опыт..." {...register('bio')} />
            {addMut.isError && <p className="text-sm text-danger">{getAddMemberErrorMessage(addMut.error)}</p>}
            <div className="flex gap-3 pt-2">
              <Button
                type="button"
                variant="secondary"
                className="flex-1"
                onClick={() => {
                  setShowAdd(false)
                  reset()
                }}
              >
                Отмена
              </Button>
              <Button type="submit" className="flex-1" loading={addMut.isPending}>
                Добавить
              </Button>
            </div>
          </form>
        </Modal>
      )}
    </div>
  )
}

// ── Settings tab ──────────────────────────────────────────────────────────────

function SettingsTab({ companyId }: { companyId: string }) {
  const qc = useQueryClient()
  const { data: companies } = useQuery({ queryKey: ['my-companies'], queryFn: companiesApi.getMy })
  const company = companies?.find((c) => c.id === companyId)
  const logoInputRef = useRef<HTMLInputElement>(null)

  const {
    register,
    handleSubmit,
    formState: { isDirty },
  } = useForm({
    values: company
      ? {
          name: company.name,
          description: company.description ?? '',
          phone: company.phone ?? '',
          email: company.email ?? '',
          allowSelfBooking: company.allowSelfBooking,
          requirePrepayment: company.requirePrepayment ?? false,
          showInPublicListing: company.showInPublicListing ?? true,
          bookingHorizonDays: company.bookingHorizonDays || '',
        }
      : undefined,
    // `values` resyncs the form whenever the `['my-companies']` cache updates — which now also
    // happens on `AddressVerifyField`'s own save (§209), a save this form's fields know nothing
    // about. Without `keepDirtyValues`, that resync silently reverts whatever the owner had typed
    // into THIS form but not yet submitted (review finding, cycle 13).
    resetOptions: { keepDirtyValues: true },
  })

  const [settingsError, setSettingsError] = useState('')

  const updateMut = useMutation({
    mutationFn: (d: Record<string, unknown>) => companiesApi.update(companyId, d),
    onSuccess: () => {
      setSettingsError('')
      qc.invalidateQueries({ queryKey: ['my-companies'] })
    },
    onError: (err: unknown) =>
      setSettingsError(getCompanyManageErrorMessage(err, 'Не удалось сохранить настройки компании.')),
  })

  const [logoError, setLogoError] = useState('')
  const logoMut = useMutation({
    mutationFn: (file: File) => companiesApi.uploadLogo(companyId, file),
    onMutate: () => setLogoError(''),
    onSuccess: () => {
      setLogoError('')
      qc.invalidateQueries({ queryKey: ['my-companies'] })
    },
    onError: (err) => setLogoError(getLogoErrorMessage(err)),
  })

  return (
    <div>
      <h2 className="text-lg font-semibold text-ink mb-4">Настройки компании</h2>
      <Card className="p-6">
        {/* Logo */}
        <div className="flex items-center gap-4 mb-6">
          {company?.logoUrl ? (
            <img
              src={company.logoUrl}
              alt="Логотип"
              className="w-16 h-16 rounded-2xl object-cover border border-line"
            />
          ) : (
            <div className="w-16 h-16 rounded-2xl bg-cream-deep flex items-center justify-center text-gold-dark font-bold text-xl">
              {company?.name?.[0] ?? '?'}
            </div>
          )}
          <div>
            <input
              ref={logoInputRef}
              type="file"
              accept="image/jpeg,image/png,image/webp"
              className="hidden"
              onChange={(e) => {
                const f = e.target.files?.[0]
                if (f) logoMut.mutate(f)
                e.target.value = ''
              }}
            />
            <Button
              size="sm"
              variant="secondary"
              loading={logoMut.isPending}
              onClick={() => logoInputRef.current?.click()}
            >
              {company?.logoUrl ? 'Заменить логотип' : 'Загрузить логотип'}
            </Button>
            <p className="text-xs text-muted mt-1">JPEG, PNG или WEBP, до 5 МБ</p>
            {logoError && <p className="text-xs text-danger mt-1">{logoError}</p>}
          </div>
        </div>

        <form
          onSubmit={handleSubmit((d) => {
            const horizon = parseBookingHorizonInput(String(d.bookingHorizonDays ?? ''))
            if (horizon.error) {
              setSettingsError(horizon.error)
              return
            }
            updateMut.mutate({ ...d, bookingHorizonDays: horizon.value })
          })}
          className="flex flex-col gap-4"
        >
          <Input label="Название" {...register('name')} />
          <div className="flex flex-col gap-1">
            <label className="text-sm font-medium text-ink-soft">Описание</label>
            <textarea
              className="rounded-xl border border-line px-3 py-2 text-sm outline-none focus:border-gold focus:ring-2 focus:ring-cream-deep resize-none"
              rows={3}
              {...register('description')}
            />
          </div>
          {/* ARCHITECTURE_CYCLE13.md §209/§211: address writes go through their own endpoint
              (`PUT /api/companies/{id}/address`), never through this form's submit — so this field
              owns its own save action instead of being `register('address')`d into `updateMut`. */}
          {company && (
            <AddressVerifyField
              companyId={companyId}
              initialAddress={company.address ?? ''}
              cityId={company.cityId}
              addressVerification={company.addressVerification}
              onSaved={() => qc.invalidateQueries({ queryKey: ['my-companies'] })}
            />
          )}
          <div className="grid grid-cols-2 gap-3">
            <Input label="Телефон" {...register('phone')} />
            <Input label="Email" type="email" {...register('email')} />
          </div>
          <div className="flex flex-col gap-1">
            <Input
              label="На сколько дней вперёд клиент может записаться"
              type="number"
              min={0}
              max={365}
              placeholder="90"
              {...register('bookingHorizonDays')}
            />
            <p className="text-xs text-muted">Пусто или 0 — 90 дней по умолчанию</p>
          </div>
          <div>
            <label className="flex items-center gap-3 cursor-pointer has-[:disabled]:cursor-not-allowed has-[:disabled]:opacity-50">
              <input
                type="checkbox"
                className="w-4 h-4 rounded accent-gold"
                disabled={company ? !company.planAllowsOnlineBooking : false}
                {...register('allowSelfBooking')}
              />
              <span className="text-sm text-ink-soft">Разрешить клиентам записываться самостоятельно</span>
            </label>
            {company && !company.planAllowsOnlineBooking && (
              <p className="text-xs text-warning mt-1 ml-7">
                Онлайн-запись не входит в текущий тариф — повысьте тариф, чтобы включить
              </p>
            )}
          </div>
          <div>
            <label className="flex items-center gap-3 cursor-pointer has-[:disabled]:cursor-not-allowed has-[:disabled]:opacity-50">
              <input
                type="checkbox"
                className="w-4 h-4 rounded accent-gold"
                disabled={company ? !company.planAllowsOnlinePayment : false}
                {...register('requirePrepayment')}
              />
              <span className="text-sm text-ink-soft">Требовать предоплату при онлайн-записи</span>
            </label>
            {company && !company.planAllowsOnlinePayment && (
              <p className="text-xs text-warning mt-1 ml-7">
                Онлайн-оплата не входит в текущий тариф — повысьте тариф, чтобы включить
              </p>
            )}
          </div>
          <div>
            <label className="flex items-center gap-3 cursor-pointer has-[:disabled]:cursor-not-allowed has-[:disabled]:opacity-50">
              <input
                type="checkbox"
                className="w-4 h-4 rounded accent-gold"
                disabled={company ? !company.planAllowsPublicListing : false}
                {...register('showInPublicListing')}
              />
              <span className="text-sm text-ink-soft">Показывать компанию в общем списке</span>
            </label>
            {company && !company.planAllowsPublicListing && (
              <p className="text-xs text-warning mt-1 ml-7">
                Отображение в общем списке не входит в текущий тариф — повысьте тариф, чтобы включить
              </p>
            )}
          </div>
          {updateMut.isSuccess && (
            <p className="text-sm text-success flex items-center gap-1.5">
              <Icon name="check" size={14} strokeWidth={2} /> Сохранено
            </p>
          )}
          {settingsError && <p className="text-sm text-danger">{settingsError}</p>}
          <Button type="submit" loading={updateMut.isPending} disabled={!isDirty}>
            Сохранить изменения
          </Button>
        </form>
      </Card>

      {company && <CityTimeZoneCard company={company} companyId={companyId} />}
      <CompanyPhotosSection companyId={companyId} />
      {company && <WidgetCard company={company} />}
      <PhotoUsageCard companyId={companyId} />
    </div>
  )
}

// ── City & time zone (US-30) ─────────────────────────────────────────────────

function CityTimeZoneCard({ company, companyId }: { company: import('../../types').Company; companyId: string }) {
  const qc = useQueryClient()
  const [city, setCity] = useState<City | null>(
    company.cityId != null && company.cityName
      ? {
          id: company.cityId,
          name: company.cityName,
          region: company.cityRegion ?? '',
          timeZoneId: company.timeZoneId ?? '',
          utcOffsetMinutes: company.utcOffsetMinutes ?? 0,
          label: company.cityRegion ? `${company.cityName}, ${company.cityRegion}` : company.cityName,
        }
      : null,
  )
  const [manualZone, setManualZone] = useState(!!company.timeZoneIsManual)
  const [zoneId, setZoneId] = useState(company.timeZoneId ?? '')
  const [error, setError] = useState('')

  const mut = useMutation({
    mutationFn: () =>
      companiesApi.update(companyId, {
        cityId: city?.id,
        // Explicit null resets to the city-derived zone (§31.3); an empty manual field means "not
        // overridden", so it's sent as null rather than an empty string.
        timeZoneId: manualZone ? zoneId || null : null,
      }),
    onSuccess: () => {
      setError('')
      qc.invalidateQueries({ queryKey: ['my-companies'] })
    },
    onError: (err: unknown) => setError(getCompanyManageErrorMessage(err, 'Не удалось сохранить город и часовой пояс.')),
  })

  const effectiveZoneId = manualZone ? zoneId : city?.timeZoneId
  const effectiveOffset = manualZone ? null : city?.utcOffsetMinutes

  return (
    <Card className="p-6 mt-[18px]">
      <h2 className="text-lg font-semibold text-ink mb-1">Город и часовой пояс</h2>
      <p className="text-sm text-muted mb-4">
        От часового пояса зависит момент отправки напоминаний клиентам — «за 24 часа» считается по местному времени
        салона, а не по Москве.
      </p>
      <div className="flex flex-col gap-3">
        <CityCombobox
          value={city}
          onChange={(c) => {
            setCity(c)
            if (c && !manualZone) setZoneId(c.timeZoneId)
          }}
        />
        {city && effectiveOffset != null && !manualZone && (
          <p className="text-xs text-muted">Часовой пояс: {formatCityTimeZone(city.label, effectiveOffset, city.timeZoneId)}</p>
        )}
        <label className="flex items-center gap-2.5 cursor-pointer">
          <input
            type="checkbox"
            className="w-4 h-4 accent-gold rounded"
            checked={manualZone}
            onChange={(e) => {
              setManualZone(e.target.checked)
              if (!e.target.checked && city) setZoneId(city.timeZoneId)
            }}
          />
          <span className="text-sm text-ink-soft">Указать часовой пояс вручную (IANA, например Asia/Barnaul)</span>
        </label>
        {manualZone && (
          <input
            value={zoneId}
            onChange={(e) => setZoneId(e.target.value)}
            placeholder="Asia/Barnaul"
            className="rounded-xl border border-line px-4 py-3 text-sm outline-none focus:border-gold bg-white text-ink font-mono"
          />
        )}
        {error && <p className="text-sm text-danger">{error}</p>}
        {mut.isSuccess && !error && (
          <p className="text-sm text-success flex items-center gap-1.5">
            <Icon name="check" size={14} strokeWidth={2} /> Сохранено
          </p>
        )}
        <Button
          className="self-start"
          loading={mut.isPending}
          disabled={!city && !effectiveZoneId}
          onClick={() => mut.mutate()}
        >
          Сохранить
        </Button>
      </div>
    </Card>
  )
}

// ── Website widget snippet (US-03) ──────────────────────────────────────────

/**
 * Escapes a string for safe use inside an HTML attribute value. Without this, a company name
 * containing `"` (or `&`/`<`/`>`) would produce a snippet that isn't valid HTML once pasted onto the
 * owner's own site — the `title="..."` attribute would end early at the embedded quote.
 */
function escapeHtmlAttribute(value: string): string {
  return value.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
}

function WidgetCard({ company }: { company: import('../../types').Company }) {
  const [copied, setCopied] = useState(false)
  // Domain comes from the current page's own address, not a hardcoded value (US-03 п. 3) — this way
  // the snippet is correct in dev, staging and prod without a build-time config knob.
  const origin = window.location.origin
  const embedUrl = `${origin}/embed/${company.slug}`
  const snippet = `<iframe src="${embedUrl}" width="100%" height="700" title="Онлайн-запись — ${escapeHtmlAttribute(company.name)}" loading="lazy" style="border:0"></iframe>`

  const copySnippet = async () => {
    try {
      await navigator.clipboard.writeText(snippet)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      // Clipboard API can be unavailable (older browsers, insecure context) — the snippet is still
      // selectable text below, so this isn't a dead end, just a missed shortcut.
    }
  }

  return (
    <Card className="p-6 mt-[18px]">
      <h2 className="text-lg font-semibold text-ink mb-1">Виджет для сайта</h2>
      <p className="text-sm text-muted mb-4">Вставьте код на свой сайт — форма записи откроется прямо там.</p>

      {(!company.allowSelfBooking || !company.onlineBookingEnabled) && (
        <div className="mb-4 rounded-xl bg-warning-bg text-warning text-sm px-4 py-3 flex items-start gap-2">
          <Icon name="alert-circle" size={15} strokeWidth={1.8} className="shrink-0 mt-0.5" />
          <span>
            {!company.onlineBookingEnabled
              ? 'Онлайн-запись не входит в текущий тариф или отключена — виджет покажет услуги, но запись из него не пройдёт.'
              : 'Самозапись клиентов выключена в настройках — виджет покажет услуги, но запись из него не пройдёт.'}
          </span>
        </div>
      )}

      <div className="flex flex-col gap-1.5 mb-4">
        <label className="text-sm font-medium text-ink-soft">Прямая ссылка</label>
        <div className="flex gap-2">
          <input
            readOnly
            value={embedUrl}
            className="flex-1 rounded-xl border border-line px-3.5 py-2.5 text-sm bg-cream-deep text-ink-soft outline-none"
          />
          <Button
            type="button"
            variant="secondary"
            onClick={() => window.open(embedUrl, '_blank', 'noopener,noreferrer')}
          >
            <Icon name="external-link" size={14} strokeWidth={1.8} /> Открыть предпросмотр
          </Button>
        </div>
      </div>

      <div className="flex flex-col gap-1.5">
        <label className="text-sm font-medium text-ink-soft">Код для вставки</label>
        <textarea
          readOnly
          rows={3}
          value={snippet}
          className="rounded-xl border border-line px-3.5 py-2.5 text-xs font-mono bg-cream-deep text-ink-soft outline-none resize-none"
        />
        <Button type="button" size="sm" variant="secondary" className="self-start mt-1" onClick={copySnippet}>
          <Icon name="copy" size={13} strokeWidth={1.8} /> {copied ? 'Скопировано' : 'Скопировать'}
        </Button>
      </div>
    </Card>
  )
}

// ── Photo storage usage (US-24) ─────────────────────────────────────────────

// API_CONTRACT_CYCLE5.md §59.1 — `Forever` was removed from the model; no entry for it here (grep
// acceptance check, §57).
const RETENTION_LABEL_RU: Record<string, string> = {
  SixMonths: '6 месяцев',
  TwelveMonths: '12 месяцев',
}

function PhotoUsageCard({ companyId }: { companyId: string }) {
  const {
    data: usage,
    isLoading,
    isError,
  } = useQuery({
    queryKey: ['company-photo-usage', companyId],
    queryFn: () => companiesApi.getPhotoUsage(companyId),
  })

  if (isLoading) return <div className="h-20 bg-cream-deep rounded-2xl animate-pulse mt-[18px]" />
  if (isError || !usage) return null

  const usedMb = usage.usedBytes / (1024 * 1024)
  const nearQuota = usage.percentUsed != null && usage.percentUsed > 90

  return (
    <Card className="p-6 mt-[18px]">
      <h2 className="text-lg font-semibold text-ink mb-1">Хранилище фото клиентов</h2>
      <p className="text-sm text-ink-soft mt-2">
        Занято {usedMb.toLocaleString('ru-RU', { maximumFractionDigits: 1 })} из{' '}
        {usage.quotaMb != null ? `${usage.quotaMb} МБ` : '∞'} · {usage.photoCount} фото
      </p>
      <p className="text-sm text-ink-soft mt-1">
        Фото хранятся {RETENTION_LABEL_RU[usage.retention] ?? usage.retention}
      </p>
      {nearQuota && (
        <div className="mt-3 rounded-xl bg-warning-bg text-warning text-sm px-4 py-3 flex items-start gap-2">
          <Icon name="alert-circle" size={15} strokeWidth={1.8} className="shrink-0 mt-0.5" />
          <span>Место под фото почти закончилось. Смените тариф, чтобы освободить больше места.</span>
        </div>
      )}
    </Card>
  )
}

// ── Main page ─────────────────────────────────────────────────────────────────

type Tab = 'services' | 'schedule' | 'members' | 'settings' | 'notifications'
type NotificationsSubTab = 'settings' | 'templates' | 'log'

function CompanyNotificationsTab({ companyId }: { companyId: string }) {
  const [sub, setSub] = useState<NotificationsSubTab>('settings')
  const subTabs: { key: NotificationsSubTab; label: string }[] = [
    { key: 'settings', label: 'Настройки' },
    { key: 'templates', label: 'Шаблоны' },
    { key: 'log', label: 'Журнал' },
  ]
  return (
    <div>
      <div className="flex gap-1 bg-cream-deep p-1 rounded-full mb-4 w-fit flex-wrap">
        {subTabs.map((t) => (
          <button
            key={t.key}
            onClick={() => setSub(t.key)}
            className={`px-3.5 py-2 rounded-full text-[13px] font-semibold whitespace-nowrap transition-all ${
              sub === t.key ? 'bg-white text-ink shadow-sm' : 'text-gold-dark hover:text-ink'
            }`}
          >
            {t.label}
          </button>
        ))}
      </div>
      {sub === 'settings' && <NotificationSettingsTab companyId={companyId} />}
      {sub === 'templates' && <NotificationTemplatesTab companyId={companyId} />}
      {sub === 'log' && <NotificationLogTab companyId={companyId} />}
    </div>
  )
}

export function CompanyManagePage() {
  const { id } = useParams<{ id: string }>()
  const [tab, setTab] = useState<Tab>('services')

  const { data: companies } = useQuery({ queryKey: ['my-companies'], queryFn: companiesApi.getMy })
  const company = companies?.find((c) => c.id === id)

  const tabs: { key: Tab; label: string }[] = [
    { key: 'services', label: 'Услуги' },
    { key: 'schedule', label: 'Расписание' },
    { key: 'members', label: 'Сотрудники' },
    { key: 'notifications', label: 'Уведомления' },
    { key: 'settings', label: 'Настройки' },
  ]

  return (
    <div className="max-w-4xl mx-auto px-8 pt-11 pb-24">
      <div className="mb-6">
        <Link to="/owner" className="text-sm text-muted hover:text-gold-dark inline-flex items-center gap-1">
          <Icon name="chevron-left" size={14} strokeWidth={1.8} /> Мои компании
        </Link>
        <h1 className="font-serif text-[26px] font-medium text-ink mt-1.5">{company?.name ?? '...'}</h1>
      </div>

      {/* Tabs */}
      <div className="flex gap-1 bg-cream-deep p-1 rounded-full mb-6 w-fit flex-wrap">
        {tabs.map((t) => (
          <button
            key={t.key}
            onClick={() => setTab(t.key)}
            className={`px-4 py-[9px] rounded-full text-sm font-semibold transition-all whitespace-nowrap ${
              tab === t.key ? 'bg-white text-ink' : 'text-gold-dark hover:text-ink'
            }`}
          >
            {t.label}
          </button>
        ))}
      </div>

      {id && tab === 'services' && <ServicesTab companyId={id} />}
      {id && tab === 'schedule' && <ScheduleTab companyId={id} />}
      {id && tab === 'members' && <MembersTab companyId={id} />}
      {id && tab === 'notifications' && <CompanyNotificationsTab companyId={id} />}
      {id && tab === 'settings' && <SettingsTab companyId={id} />}
    </div>
  )
}
