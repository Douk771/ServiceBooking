import { useState } from 'react'
import { useParams, Link } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { companiesApi } from '../../api/companies'
import { servicesApi, type CreateServicePayload } from '../../api/services'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { Input } from '../../components/ui/Input'
import { Modal } from '../../components/ui/Modal'
import { ScheduleTab } from './ScheduleTab'
import type { Service } from '../../types'

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

  const closeForm = () => { setEditing(null); setShowAdd(false); reset() }

  const createMut = useMutation({
    mutationFn: (d: ServiceFormData) =>
      servicesApi.create({ companyId, name: d.name, description: d.description || undefined, durationMinutes: +d.durationMinutes, price: +d.price }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['services', companyId] }); closeForm() },
  })

  const updateMut = useMutation({
    mutationFn: (d: ServiceFormData) =>
      servicesApi.update(editing!.id, { companyId, name: d.name, description: d.description || undefined, durationMinutes: +d.durationMinutes, price: +d.price }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['services', companyId] }); closeForm() },
  })

  const deleteMut = useMutation({
    mutationFn: (id: string) => servicesApi.delete(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['services', companyId] }),
  })

  const onSubmit = (d: ServiceFormData) => editing ? updateMut.mutate(d) : createMut.mutate(d)
  const isPending = createMut.isPending || updateMut.isPending

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <h2 className="text-lg font-semibold text-gray-900">Услуги</h2>
        <Button size="sm" onClick={() => setShowAdd(true)}>+ Добавить</Button>
      </div>

      {isLoading ? (
        <div className="grid gap-3">
          {Array.from({ length: 3 }).map((_, i) => <div key={i} className="h-16 bg-gray-100 rounded-2xl animate-pulse" />)}
        </div>
      ) : services && services.length > 0 ? (
        <div className="grid gap-3">
          {services.map((s) => (
            <Card key={s.id} className="p-4 flex items-center justify-between gap-4">
              <div>
                <p className="font-medium text-gray-900">{s.name}</p>
                <p className="text-sm text-gray-400">⏱ {s.durationMinutes} мин · {s.price.toLocaleString('ru-RU')} ₽</p>
                {s.description && <p className="text-xs text-gray-400 mt-0.5 line-clamp-1">{s.description}</p>}
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
        <Card className="p-12 text-center text-gray-400">
          <p className="text-3xl mb-2">🛠️</p>
          <p>Услуги ещё не добавлены</p>
        </Card>
      )}

      {(showAdd || editing) && (
        <Modal title={editing ? 'Редактировать услугу' : 'Добавить услугу'} onClose={closeForm}>
          <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
            <Input
              label="Название *"
              placeholder="Стрижка мужская"
              error={errors.name?.message}
              {...register('name', { required: 'Введите название' })}
            />
            <div className="flex flex-col gap-1">
              <label className="text-sm font-medium text-gray-700">Описание</label>
              <textarea
                className="rounded-xl border border-gray-200 px-3 py-2 text-sm outline-none focus:border-primary-400 focus:ring-2 focus:ring-primary-100 resize-none"
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

function MembersTab({ companyId }: { companyId: string }) {
  const qc = useQueryClient()
  const [showAdd, setShowAdd] = useState(false)
  const { register, handleSubmit, reset } = useForm<{ email: string; role: string; bio: string }>({
    defaultValues: { role: 'Master' }
  })

  const { data: members, isLoading } = useQuery({
    queryKey: ['company-members', companyId],
    queryFn: () => companiesApi.getMembers(companyId),
  })

  const addMut = useMutation({
    mutationFn: (d: { email: string; role: string; bio: string }) =>
      companiesApi.addMember(companyId, d.email, d.role, d.bio || undefined),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['company-members', companyId] }); setShowAdd(false); reset() },
  })

  const removeMut = useMutation({
    mutationFn: (memberId: string) => companiesApi.removeMember(companyId, memberId),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['company-members', companyId] }),
  })

  const roleLabel: Record<string, string> = { Master: 'Мастер', CompanyOwner: 'Владелец' }

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <h2 className="text-lg font-semibold text-gray-900">Сотрудники</h2>
        <Button size="sm" onClick={() => setShowAdd(true)}>+ Добавить</Button>
      </div>

      {isLoading ? (
        <div className="grid gap-3">
          {Array.from({ length: 2 }).map((_, i) => <div key={i} className="h-16 bg-gray-100 rounded-2xl animate-pulse" />)}
        </div>
      ) : members && members.length > 0 ? (
        <div className="grid gap-3">
          {members.map((m) => (
            <Card key={m.id} className="p-4 flex items-center justify-between gap-4">
              <div className="flex items-center gap-3">
                <div className="w-10 h-10 rounded-full bg-primary-100 flex items-center justify-center text-primary-700 font-semibold text-sm shrink-0">
                  {m.firstName[0]}{m.lastName[0]}
                </div>
                <div>
                  <p className="font-medium text-gray-900">{m.firstName} {m.lastName}</p>
                  <p className="text-sm text-gray-400">{m.email} · {roleLabel[m.role] ?? m.role}</p>
                  {m.bio && <p className="text-xs text-gray-400 mt-0.5">{m.bio}</p>}
                </div>
              </div>
              <Button variant="danger" size="sm" loading={removeMut.isPending} onClick={() => removeMut.mutate(m.id)}>
                Удалить
              </Button>
            </Card>
          ))}
        </div>
      ) : (
        <Card className="p-12 text-center text-gray-400">
          <p className="text-3xl mb-2">👥</p>
          <p>Сотрудники ещё не добавлены</p>
        </Card>
      )}

      {showAdd && (
        <Modal title="Добавить сотрудника" onClose={() => { setShowAdd(false); reset() }}>
          <form onSubmit={handleSubmit((d) => addMut.mutate(d))} className="flex flex-col gap-4">
            <Input label="Email пользователя *" type="email" placeholder="master@example.com" {...register('email', { required: true })} />
            <div className="flex flex-col gap-1">
              <label className="text-sm font-medium text-gray-700">Роль</label>
              <select
                className="rounded-xl border border-gray-200 px-3 py-2 text-sm outline-none focus:border-primary-400 focus:ring-2 focus:ring-primary-100"
                {...register('role')}
              >
                <option value="Master">Мастер</option>
                <option value="CompanyOwner">Совладелец</option>
              </select>
            </div>
            <Input label="О мастере" placeholder="Специализация, опыт..." {...register('bio')} />
            {addMut.isError && <p className="text-sm text-red-500">Пользователь не найден или уже является сотрудником</p>}
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

  const { register, handleSubmit, formState: { isDirty } } = useForm({
    values: company ? {
      name: company.name,
      description: company.description ?? '',
      address: company.address ?? '',
      phone: company.phone ?? '',
      email: company.email ?? '',
      allowSelfBooking: company.allowSelfBooking,
    } : undefined,
  })

  const updateMut = useMutation({
    mutationFn: (d: Record<string, unknown>) => companiesApi.update(companyId, d),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['my-companies'] }),
  })

  return (
    <div>
      <h2 className="text-lg font-semibold text-gray-900 mb-4">Настройки компании</h2>
      <Card className="p-6">
        <form onSubmit={handleSubmit((d) => updateMut.mutate(d))} className="flex flex-col gap-4">
          <Input label="Название" {...register('name')} />
          <div className="flex flex-col gap-1">
            <label className="text-sm font-medium text-gray-700">Описание</label>
            <textarea
              className="rounded-xl border border-gray-200 px-3 py-2 text-sm outline-none focus:border-primary-400 focus:ring-2 focus:ring-primary-100 resize-none"
              rows={3}
              {...register('description')}
            />
          </div>
          <Input label="Адрес" {...register('address')} />
          <div className="grid grid-cols-2 gap-3">
            <Input label="Телефон" {...register('phone')} />
            <Input label="Email" type="email" {...register('email')} />
          </div>
          <label className="flex items-center gap-3 cursor-pointer">
            <input type="checkbox" className="w-4 h-4 rounded accent-orange-500" {...register('allowSelfBooking')} />
            <span className="text-sm text-gray-700">Разрешить клиентам записываться самостоятельно</span>
          </label>
          {updateMut.isSuccess && <p className="text-sm text-green-600">✓ Сохранено</p>}
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
    { key: 'services',  label: '🛠 Услуги' },
    { key: 'schedule',  label: '📅 Расписание' },
    { key: 'members',   label: '👥 Сотрудники' },
    { key: 'settings',  label: '⚙️ Настройки' },
  ]

  return (
    <div className="max-w-4xl mx-auto px-4 py-8">
      <div className="mb-6">
        <Link to="/owner" className="text-sm text-gray-500 hover:text-gray-700">← Мои компании</Link>
        <h1 className="text-2xl font-bold text-gray-900 mt-1">{company?.name ?? '...'}</h1>
      </div>

      {/* Tabs */}
      <div className="flex gap-1 bg-gray-100 p-1 rounded-2xl mb-6 w-fit">
        {tabs.map((t) => (
          <button
            key={t.key}
            onClick={() => setTab(t.key)}
            className={`px-4 py-2 rounded-xl text-sm font-medium transition-all ${
              tab === t.key ? 'bg-white text-gray-900 shadow-sm' : 'text-gray-500 hover:text-gray-700'
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
