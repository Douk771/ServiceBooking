import { useRef, useState } from 'react'
import { useParams, Link } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { AxiosError } from 'axios'
import { companiesApi } from '../../api/companies'
import { servicesApi } from '../../api/services'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { Input } from '../../components/ui/Input'
import { Modal } from '../../components/ui/Modal'
import { Icon } from '../../components/ui/Icon'
import { ScheduleTab } from './ScheduleTab'
import { getAddMemberErrorMessage } from '../../utils/memberError'
import type { Service } from '../../types'

// Generic fallback for mutations on this page that don't have a dedicated *Error.ts mapper:
// the backend's text/plain bodies (400/403/409) are shown verbatim when present, otherwise a
// Russian fallback describes the action that failed.
function mutationErrorText(err: unknown, fallback: string): string {
  const ax = err as AxiosError
  const data = ax?.response?.data
  return typeof data === 'string' && data ? data : fallback
}

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

  const { data: services, isLoading } = useQuery({
    queryKey: ['services', companyId],
    queryFn: () => servicesApi.getByCompany(companyId),
  })

  const { register, handleSubmit, reset, setValue, formState: { errors } } = useForm<ServiceFormData>()

  const openEdit = (s: Service) => {
    setEditing(s)
    setValue('name', s.name)
    setValue('description', s.description ?? '')
    setValue('durationMinutes', s.durationMinutes)
    setValue('price', s.price)
  }

  const closeForm = () => { setEditing(null); setShowAdd(false); setFormError(''); reset() }

  const createMut = useMutation({
    mutationFn: (d: ServiceFormData) =>
      servicesApi.create({ companyId, name: d.name, description: d.description || undefined, durationMinutes: +d.durationMinutes, price: +d.price }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['services', companyId] }); closeForm() },
    onError: (err: unknown) => setFormError(mutationErrorText(err, 'Не удалось сохранить услугу. Попробуйте снова.')),
  })

  const updateMut = useMutation({
    mutationFn: (d: ServiceFormData) =>
      servicesApi.update(editing!.id, { companyId, name: d.name, description: d.description || undefined, durationMinutes: +d.durationMinutes, price: +d.price }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['services', companyId] }); closeForm() },
    onError: (err: unknown) => setFormError(mutationErrorText(err, 'Не удалось сохранить услугу. Попробуйте снова.')),
  })

  const deleteMut = useMutation({
    mutationFn: (id: string) => servicesApi.delete(id),
    onSuccess: () => { setDeleteError(''); qc.invalidateQueries({ queryKey: ['services', companyId] }) },
    onError: (err: unknown) => setDeleteError(mutationErrorText(err, 'Не удалось удалить услугу. Попробуйте снова.')),
  })

  const onSubmit = (d: ServiceFormData) => editing ? updateMut.mutate(d) : createMut.mutate(d)
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
          {Array.from({ length: 3 }).map((_, i) => <div key={i} className="h-16 bg-cream-deep rounded-2xl animate-pulse" />)}
        </div>
      ) : services && services.length > 0 ? (
        <div className="grid gap-3">
          {services.map((s) => (
            <Card key={s.id} className="p-4 flex items-center justify-between gap-4">
              <div>
                <p className="font-medium text-ink">{s.name}</p>
                <p className="text-sm text-muted flex items-center gap-1">
                  <Icon name="clock" size={13} strokeWidth={1.7} /> {s.durationMinutes} мин · {s.price.toLocaleString('ru-RU')} ₽
                </p>
                {s.description && <p className="text-xs text-muted mt-0.5 line-clamp-1">{s.description}</p>}
              </div>
              <div className="flex gap-2 shrink-0">
                <Button variant="secondary" size="sm" onClick={() => openEdit(s)}>Изменить</Button>
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
                {...register('durationMinutes', { required: 'Введите длительность', min: { value: 5, message: 'Минимум 5 мин' } })}
              />
              <Input
                label="Цена (₽) *"
                type="number"
                min={0}
                step={50}
                placeholder="1500"
                error={errors.price?.message}
                {...register('price', { required: 'Введите цену', min: { value: 0, message: 'Цена не может быть отрицательной' } })}
              />
            </div>
            {formError && <p className="text-sm text-danger">{formError}</p>}
            <div className="flex gap-3 pt-2">
              <Button type="button" variant="secondary" className="flex-1" onClick={closeForm}>Отмена</Button>
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

  const toggle = (id: string) => {
    setSelected(prev => {
      const next = new Set(prev)
      next.has(id) ? next.delete(id) : next.add(id)
      return next
    })
    setDirty(true)
  }

  const saveMut = useMutation({
    mutationFn: () => companiesApi.updateMemberServices(companyId, m.id, [...selected]),
    onSuccess: () => { setSaveError(''); setDirty(false); qc.invalidateQueries({ queryKey: ['company-members', companyId] }) },
    onError: (err: unknown) => setSaveError(mutationErrorText(err, 'Не удалось сохранить услуги сотрудника.')),
  })

  const commissionMut = useMutation({
    mutationFn: () => companiesApi.updateMemberCommission(companyId, m.id, commission),
    onSuccess: () => { setCommissionError(''); setCommissionDirty(false); qc.invalidateQueries({ queryKey: ['company-members', companyId] }) },
    onError: (err: unknown) => setCommissionError(mutationErrorText(err, 'Не удалось сохранить комиссию.')),
  })

  return (
    <Card className="overflow-hidden">
      <div className="p-4 flex items-center justify-between gap-4">
        <div className="flex items-center gap-3 min-w-0">
          <div className="w-10 h-10 rounded-full bg-cream-deep flex items-center justify-center text-gold-dark font-semibold text-sm shrink-0">
            {m.firstName[0]}{m.lastName[0]}
          </div>
          <div className="min-w-0">
            <p className="font-medium text-ink">{m.firstName} {m.lastName}</p>
            <p className="text-sm text-muted truncate">{m.phone || m.email} · {roleLabel[m.role] ?? m.role}</p>
            {m.bio && <p className="text-xs text-muted mt-0.5 truncate">{m.bio}</p>}
            {commissionError && <p className="text-xs text-danger mt-0.5">{commissionError}</p>}
          </div>
        </div>
        <div className="flex items-center gap-2 shrink-0">
          <div className="flex items-center gap-1.5">
            <label className="text-xs text-muted whitespace-nowrap">Комиссия:</label>
            <input
              type="number"
              min={0}
              max={100}
              step={1}
              value={commission}
              onChange={(e) => { setCommission(Number(e.target.value)); setCommissionDirty(true) }}
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
              onClick={() => setExpanded(e => !e)}
              className="flex items-center gap-1 text-xs text-muted hover:text-gold-dark border border-line hover:border-line-strong rounded-lg px-2 py-1 transition-colors"
            >
              Услуги {selected.size > 0 ? `(${selected.size})` : ''}
              <Icon name="chevron-down" size={12} strokeWidth={1.8} className={`transition-transform ${expanded ? 'rotate-180' : ''}`} />
            </button>
          )}
          <Button variant="danger" size="sm" loading={removeLoading} onClick={() => onRemove(m.id)}>
            Удалить
          </Button>
        </div>
      </div>

      {expanded && (
        <div className="border-t border-line px-4 py-3 bg-cream-deep">
          <p className="text-xs font-medium text-muted uppercase tracking-wide mb-2">Услуги сотрудника</p>
          <div className="flex flex-wrap gap-2 mb-3">
            {services.map(s => {
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
                  <input
                    type="checkbox"
                    className="sr-only"
                    checked={checked}
                    onChange={() => toggle(s.id)}
                  />
                  <span className={`w-3.5 h-3.5 rounded border flex items-center justify-center shrink-0 ${checked ? 'bg-gold-dark border-gold-dark' : 'border-line-strong'}`}>
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

function MembersTab({ companyId }: { companyId: string }) {
  const qc = useQueryClient()
  const [showAdd, setShowAdd] = useState(false)
  const [removeError, setRemoveError] = useState('')
  const { register, handleSubmit, reset } = useForm<{ phone: string; firstName: string; lastName: string; role: string; bio: string; email: string }>({
    defaultValues: { role: 'Master' }
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

  // Mirrors the seat-limit check in CompaniesController.AddMember (counts ALL members, owner
  // included) — checked on click, before the owner spends time filling out the add-member form.
  const atMemberLimit = !!company?.maxEmployees && (members?.length ?? 0) >= company.maxEmployees

  const addMut = useMutation({
    mutationFn: (d: { phone: string; firstName: string; lastName: string; role: string; bio: string; email: string }) =>
      companiesApi.addMember(companyId, d.phone, d.firstName, d.lastName, d.role, d.bio || undefined, d.email || undefined),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['company-members', companyId] }); setShowAdd(false); reset() },
  })

  const removeMut = useMutation({
    mutationFn: (memberId: string) => companiesApi.removeMember(companyId, memberId),
    onSuccess: () => { setRemoveError(''); qc.invalidateQueries({ queryKey: ['company-members', companyId] }) },
    onError: (err: unknown) => setRemoveError(mutationErrorText(err, 'Не удалось удалить сотрудника.')),
  })

  return (
    <div>
      <div className="flex items-start justify-between mb-4">
        <h2 className="text-lg font-semibold text-ink">Сотрудники</h2>
        <div className="flex flex-col items-end gap-1">
          <Button size="sm" onClick={() => setShowAdd(true)} disabled={atMemberLimit}>
            <Icon name="plus" size={14} strokeWidth={2} /> Добавить
          </Button>
          {company?.maxEmployees != null && (
            <p className="text-xs text-muted">{members?.length ?? 0} / {company.maxEmployees} сотрудников</p>
          )}
        </div>
      </div>
      {atMemberLimit && (
        <p className="text-xs text-warning mb-4 -mt-2">
          Достигнут лимит сотрудников по текущему тарифу — повысьте тариф, чтобы добавить ещё
        </p>
      )}
      {removeError && <p className="text-sm text-danger mb-4 -mt-2">{removeError}</p>}

      {isLoading ? (
        <div className="grid gap-3">
          {Array.from({ length: 2 }).map((_, i) => <div key={i} className="h-16 bg-cream-deep rounded-2xl animate-pulse" />)}
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
        <Modal title="Добавить сотрудника" onClose={() => { setShowAdd(false); reset() }}>
          <form onSubmit={handleSubmit((d) => addMut.mutate(d))} className="flex flex-col gap-4">
            <p className="text-xs text-muted bg-warning-bg border border-[#EAD9AC] rounded-xl px-3 py-2">
              Если пользователь ещё не зарегистрирован — аккаунт будет создан автоматически.<br />
              Временный пароль: <strong>Sb + последние 6 цифр телефона</strong> (например, <em>+7 999 123‑45‑67</em> → <em>Sb234567</em>)
            </p>
            <Input label="Телефон *" type="tel" placeholder="+7 999 000 00 00" {...register('phone', { required: true })} />
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
              <Button type="button" variant="secondary" className="flex-1" onClick={() => { setShowAdd(false); reset() }}>Отмена</Button>
              <Button type="submit" className="flex-1" loading={addMut.isPending}>Добавить</Button>
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

  const { register, handleSubmit, formState: { isDirty } } = useForm({
    values: company ? {
      name: company.name,
      description: company.description ?? '',
      address: company.address ?? '',
      phone: company.phone ?? '',
      email: company.email ?? '',
      allowSelfBooking: company.allowSelfBooking,
      requirePrepayment: company.requirePrepayment ?? false,
      showInPublicListing: company.showInPublicListing ?? true,
    } : undefined,
  })

  const [settingsError, setSettingsError] = useState('')

  const updateMut = useMutation({
    mutationFn: (d: Record<string, unknown>) => companiesApi.update(companyId, d),
    onSuccess: () => { setSettingsError(''); qc.invalidateQueries({ queryKey: ['my-companies'] }) },
    onError: (err: unknown) => setSettingsError(mutationErrorText(err, 'Не удалось сохранить настройки компании.')),
  })

  const logoMut = useMutation({
    mutationFn: (file: File) => companiesApi.uploadLogo(companyId, file),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['my-companies'] }),
  })

  return (
    <div>
      <h2 className="text-lg font-semibold text-ink mb-4">Настройки компании</h2>
      <Card className="p-6">
        {/* Logo */}
        <div className="flex items-center gap-4 mb-6">
          {company?.logoUrl ? (
            <img src={company.logoUrl} alt="Логотип" className="w-16 h-16 rounded-2xl object-cover border border-line" />
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
              onChange={e => { const f = e.target.files?.[0]; if (f) logoMut.mutate(f); e.target.value = '' }}
            />
            <Button size="sm" variant="secondary" loading={logoMut.isPending} onClick={() => logoInputRef.current?.click()}>
              {company?.logoUrl ? 'Заменить логотип' : 'Загрузить логотип'}
            </Button>
            <p className="text-xs text-muted mt-1">JPEG, PNG или WEBP, до 5 МБ</p>
            {logoMut.isError && <p className="text-xs text-danger mt-1">Не удалось загрузить изображение</p>}
          </div>
        </div>

        <form onSubmit={handleSubmit((d) => updateMut.mutate(d))} className="flex flex-col gap-4">
          <Input label="Название" {...register('name')} />
          <div className="flex flex-col gap-1">
            <label className="text-sm font-medium text-ink-soft">Описание</label>
            <textarea
              className="rounded-xl border border-line px-3 py-2 text-sm outline-none focus:border-gold focus:ring-2 focus:ring-cream-deep resize-none"
              rows={3}
              {...register('description')}
            />
          </div>
          <Input label="Адрес" {...register('address')} />
          <div className="grid grid-cols-2 gap-3">
            <Input label="Телефон" {...register('phone')} />
            <Input label="Email" type="email" {...register('email')} />
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
              <p className="text-xs text-warning mt-1 ml-7">Онлайн-запись не входит в текущий тариф — повысьте тариф, чтобы включить</p>
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
              <p className="text-xs text-warning mt-1 ml-7">Онлайн-оплата не входит в текущий тариф — повысьте тариф, чтобы включить</p>
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
              <p className="text-xs text-warning mt-1 ml-7">Отображение в общем списке не входит в текущий тариф — повысьте тариф, чтобы включить</p>
            )}
          </div>
          {updateMut.isSuccess && (
            <p className="text-sm text-success flex items-center gap-1.5">
              <Icon name="check" size={14} strokeWidth={2} /> Сохранено
            </p>
          )}
          {settingsError && <p className="text-sm text-danger">{settingsError}</p>}
          <Button type="submit" loading={updateMut.isPending} disabled={!isDirty}>Сохранить изменения</Button>
        </form>
      </Card>
    </div>
  )
}

// ── Main page ─────────────────────────────────────────────────────────────────

type Tab = 'services' | 'schedule' | 'members' | 'settings'

export function CompanyManagePage() {
  const { id } = useParams<{ id: string }>()
  const [tab, setTab] = useState<Tab>('services')

  const { data: companies } = useQuery({ queryKey: ['my-companies'], queryFn: companiesApi.getMy })
  const company = companies?.find((c) => c.id === id)

  const tabs: { key: Tab; label: string }[] = [
    { key: 'services',  label: 'Услуги' },
    { key: 'schedule',  label: 'Расписание' },
    { key: 'members',   label: 'Сотрудники' },
    { key: 'settings',  label: 'Настройки' },
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

      {id && tab === 'services'  && <ServicesTab  companyId={id} />}
      {id && tab === 'schedule'  && <ScheduleTab  companyId={id} />}
      {id && tab === 'members'   && <MembersTab   companyId={id} />}
      {id && tab === 'settings'  && <SettingsTab  companyId={id} />}
    </div>
  )
}
