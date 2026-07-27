import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { format } from 'date-fns'
import { companiesApi, type CreateCompanyPayload } from '../api/companies'
import { adminApi } from '../api/admin'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { Input } from '../components/ui/Input'
import { Modal } from '../components/ui/Modal'
import { ScheduleTab } from './owner/ScheduleTab'
import { DashboardTab } from './owner/DashboardTab'
import { MailingTab } from './owner/MailingTab'
import { MasterClientsPage } from './MasterClientsPage'
import { useAuthStore } from '../store/authStore'
import { getCreateCompanyErrorMessage } from '../utils/companyError'

function slugify(str: string) {
  return str
    .toLowerCase()
    .replace(/[а-яё]/g, (c: string) => (({ а:'a',б:'b',в:'v',г:'g',д:'d',е:'e',ё:'yo',ж:'zh',з:'z',и:'i',й:'j',к:'k',л:'l',м:'m',н:'n',о:'o',п:'p',р:'r',с:'s',т:'t',у:'u',ф:'f',х:'h',ц:'ts',ч:'ch',ш:'sh',щ:'sch',ъ:'',ы:'y',ь:'',э:'e',ю:'yu',я:'ya' } as Record<string,string>)[c] ?? c))
    .replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '')
}

// ── My companies (owner) ──────────────────────────────────────────────────────

function MyCompaniesTab() {
  const [showCreate, setShowCreate] = useState(false)
  const qc = useQueryClient()
  const { data: companies, isLoading } = useQuery({ queryKey: ['my-companies'], queryFn: companiesApi.getMy })

  interface FormData { name: string; slug: string; description: string; address: string; phone: string; email: string; allowSelfBooking: boolean; showInPublicListing: boolean }
  const { register, handleSubmit, setValue, reset, formState: { errors } } = useForm<FormData>({ defaultValues: { allowSelfBooking: true, showInPublicListing: true } })

  const create = useMutation({
    mutationFn: (data: CreateCompanyPayload) => companiesApi.create(data),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['my-companies'] }); setShowCreate(false); reset() },
  })

  return (
    <div>
      <div className="flex items-center justify-between mb-5">
        <p className="text-sm text-gray-500">Компании, которыми вы владеете</p>
        <Button onClick={() => setShowCreate(true)}>+ Создать компанию</Button>
      </div>

      {isLoading ? (
        <div className="grid gap-4">{Array.from({length:2}).map((_,i)=><div key={i} className="h-24 bg-gray-100 rounded-2xl animate-pulse"/>)}</div>
      ) : companies && companies.length > 0 ? (
        <div className="grid gap-4">
          {companies.map(c => (
            <Card key={c.id} className="p-5 flex items-center justify-between gap-4 flex-wrap">
              <div className="flex items-center gap-4">
                <div className="w-12 h-12 rounded-2xl bg-gradient-to-br from-primary-400 to-accent-500 flex items-center justify-center text-white text-lg font-bold shrink-0">{c.name[0]}</div>
                <div>
                  <h3 className="font-semibold text-gray-900">{c.name}</h3>
                  <p className="text-sm text-gray-400">/{c.slug}</p>
                  <span className={`text-xs font-medium ${c.allowSelfBooking ? 'text-green-600' : 'text-amber-600'}`}>
                    {c.allowSelfBooking ? '✓ Онлайн-запись' : '✗ Только через мастера'}
                  </span>
                </div>
              </div>
              <div className="flex gap-2 shrink-0">
                <Link to={`/owner/company/${c.id}`}><Button variant="secondary" size="sm">Управление</Button></Link>
                <Link to={`/company/${c.slug}`} target="_blank"><Button variant="ghost" size="sm">Открыть →</Button></Link>
              </div>
            </Card>
          ))}
        </div>
      ) : (
        <Card className="p-12 text-center text-gray-400">
          <p className="text-4xl mb-3">🏪</p>
          <p className="text-lg font-medium text-gray-600">У вас ещё нет компаний</p>
          <Button className="mt-4" onClick={() => setShowCreate(true)}>Создать компанию</Button>
        </Card>
      )}

      {showCreate && (
        <Modal title="Создать компанию" onClose={() => { setShowCreate(false); reset() }}>
          <form onSubmit={handleSubmit(d => create.mutate({ name:d.name, slug:d.slug||slugify(d.name), description:d.description||undefined, address:d.address||undefined, phone:d.phone||undefined, email:d.email||undefined, allowSelfBooking:d.allowSelfBooking, showInPublicListing:d.showInPublicListing }))} className="flex flex-col gap-4">
            <Input label="Название *" placeholder="Салон красоты «Розы»" error={errors.name?.message}
              {...register('name', { required: 'Введите название', onChange: e => setValue('slug', slugify(e.target.value)) })} />
            <Input label="URL-адрес (slug) *" placeholder="rozy-salon" error={errors.slug?.message} {...register('slug', { required: true })} />
            <div className="flex flex-col gap-1">
              <label className="text-sm font-medium text-gray-700">Описание</label>
              <textarea rows={2} className="rounded-xl border border-gray-200 px-3 py-2 text-sm outline-none focus:border-primary-400 resize-none" {...register('description')} />
            </div>
            <Input label="Адрес" {...register('address')} />
            <div className="grid grid-cols-2 gap-3">
              <Input label="Телефон" {...register('phone')} />
              <Input label="Email" type="email" {...register('email')} />
            </div>
            <label className="flex items-center gap-3 cursor-pointer">
              <input type="checkbox" className="w-4 h-4 accent-orange-500" {...register('allowSelfBooking')} />
              <span className="text-sm text-gray-700">Разрешить клиентам записываться самостоятельно</span>
            </label>
            <label className="flex items-center gap-3 cursor-pointer">
              <input type="checkbox" className="w-4 h-4 accent-orange-500" {...register('showInPublicListing')} />
              <span className="text-sm text-gray-700">Показывать компанию в общем списке</span>
            </label>
            {create.isError && <p className="text-sm text-red-500">{getCreateCompanyErrorMessage(create.error)}</p>}
            <div className="flex gap-3 pt-1">
              <Button type="button" variant="secondary" className="flex-1" onClick={() => { setShowCreate(false); reset() }}>Отмена</Button>
              <Button type="submit" className="flex-1" loading={create.isPending}>Создать</Button>
            </div>
          </form>
        </Modal>
      )}
    </div>
  )
}

// ── Schedule (master) ─────────────────────────────────────────────────────────

function ScheduleSection() {
  const { user, hasRole } = useAuthStore()
  const isOwner = hasRole('CompanyOwner') || hasRole('SuperAdmin')
  const { data: companies, isLoading } = useQuery({ queryKey: ['member-companies'], queryFn: companiesApi.getMemberOf })
  const [selectedId, setSelectedId] = useState('')
  const companyId = selectedId || companies?.[0]?.id || ''

  if (isLoading) return <div className="h-40 bg-gray-100 rounded-2xl animate-pulse" />
  if (!companies?.length) return <Card className="p-12 text-center text-gray-400"><p className="text-3xl mb-2">🏢</p><p>Вы не привязаны ни к одной компании</p></Card>

  return (
    <div>
      {companies.length > 1 && (
        <div className="flex flex-wrap gap-2 mb-4">
          {companies.map(c => (
            <button key={c.id} onClick={() => setSelectedId(c.id)}
              className={`px-4 py-2 rounded-xl border text-sm font-medium transition-all ${c.id === companyId ? 'bg-primary-50 border-primary-300 text-primary-700' : 'bg-white border-gray-200 text-gray-500 hover:border-gray-300'}`}>
              {c.name}
            </button>
          ))}
        </div>
      )}
      {companyId && (
        <ScheduleTab
          companyId={companyId}
          selfMasterId={isOwner ? undefined : user?.id}
        />
      )}
    </div>
  )
}

// ── Clients (master/owner) ────────────────────────────────────────────────────

function ClientsSection() {
  const { data: companies, isLoading } = useQuery({ queryKey: ['member-companies'], queryFn: companiesApi.getMemberOf })
  const [selectedId, setSelectedId] = useState('')
  const companyId = selectedId || companies?.[0]?.id || ''

  if (isLoading) return <div className="h-40 bg-gray-100 rounded-2xl animate-pulse" />
  if (!companies?.length) return <Card className="p-12 text-center text-gray-400"><p className="text-3xl mb-2">🏢</p><p>Вы не привязаны ни к одной компании</p></Card>

  return (
    <div>
      {companies.length > 1 && (
        <div className="flex flex-wrap gap-2 mb-4">
          {companies.map(c => (
            <button key={c.id} onClick={() => setSelectedId(c.id)}
              className={`px-4 py-2 rounded-xl border text-sm font-medium transition-all ${c.id === companyId ? 'bg-primary-50 border-primary-300 text-primary-700' : 'bg-white border-gray-200 text-gray-500 hover:border-gray-300'}`}>
              {c.name}
            </button>
          ))}
        </div>
      )}
      {companyId && <MasterClientsPage companyId={companyId} />}
    </div>
  )
}

// ── Dashboard (owner) ─────────────────────────────────────────────────────────

function DashboardSection() {
  const { data: companies, isLoading } = useQuery({ queryKey: ['my-companies'], queryFn: companiesApi.getMy })

  if (isLoading) return <div className="h-40 bg-gray-100 rounded-2xl animate-pulse" />
  if (!companies?.length) return <Card className="p-12 text-center text-gray-400"><p className="text-3xl mb-2">🏢</p><p>У вас нет компаний</p></Card>

  return <DashboardTab companies={companies} />
}

// ── Mailing (owner) ───────────────────────────────────────────────────────────

function MailingSection() {
  const { data: companies, isLoading } = useQuery({ queryKey: ['my-companies'], queryFn: companiesApi.getMy })
  const [selectedId, setSelectedId] = useState('')
  const companyId = selectedId || companies?.[0]?.id || ''

  if (isLoading) return <div className="h-40 bg-gray-100 rounded-2xl animate-pulse" />
  if (!companies?.length) return <Card className="p-12 text-center text-gray-400"><p className="text-3xl mb-2">🏢</p><p>У вас нет компаний</p></Card>

  return (
    <div>
      {companies.length > 1 && (
        <div className="flex flex-wrap gap-2 mb-4">
          {companies.map(c => (
            <button key={c.id} onClick={() => setSelectedId(c.id)}
              className={`px-4 py-2 rounded-xl border text-sm font-medium transition-all ${c.id === companyId ? 'bg-primary-50 border-primary-300 text-primary-700' : 'bg-white border-gray-200 text-gray-500 hover:border-gray-300'}`}>
              {c.name}
            </button>
          ))}
        </div>
      )}
      {companyId && <MailingTab companyId={companyId} />}
    </div>
  )
}

// ── Reports (owner) ───────────────────────────────────────────────────────────

function ReportsTab() {
  const { data: companies } = useQuery({ queryKey: ['my-companies'], queryFn: companiesApi.getMy })
  const [companyId, setCompanyId] = useState('')
  const [from, setFrom] = useState(format(new Date(), 'yyyy-MM-01'))
  const [to, setTo] = useState(format(new Date(), 'yyyy-MM-dd'))

  const selectedId = companyId || companies?.[0]?.id || ''

  const { data, isLoading, refetch } = useQuery({
    queryKey: ['masters-report', selectedId, from, to],
    queryFn: () => adminApi.getMastersReport(selectedId, from, to),
    enabled: false,
  })

  return (
    <div>
      <div className="flex flex-wrap gap-3 mb-5">
        {companies && companies.length > 1 && (
          <select value={selectedId} onChange={e => setCompanyId(e.target.value)} className="rounded-xl border border-gray-200 px-3 py-2 text-sm outline-none focus:border-primary-400">
            {companies.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        )}
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-gray-500">С</label>
          <input type="date" value={from} onChange={e => setFrom(e.target.value)} className="rounded-xl border border-gray-200 px-3 py-2 text-sm outline-none focus:border-primary-400" />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-gray-500">По</label>
          <input type="date" value={to} onChange={e => setTo(e.target.value)} className="rounded-xl border border-gray-200 px-3 py-2 text-sm outline-none focus:border-primary-400" />
        </div>
        <div className="flex items-end">
          <Button onClick={() => refetch()} disabled={!selectedId}>Построить отчёт</Button>
        </div>
      </div>

      {isLoading ? (
        <div className="grid gap-3">{Array.from({length:3}).map((_,i)=><div key={i} className="h-20 bg-gray-100 rounded-2xl animate-pulse"/>)}</div>
      ) : data ? (
        <>
          {data.length === 0 ? (
            <p className="text-center text-gray-400 py-8">Нет завершённых записей за период</p>
          ) : (
            <>
              <div className="grid gap-3 mb-4">
                {data.map(r => (
                  <Card key={r.masterId} className="p-4">
                    <div className="flex items-center justify-between gap-4 flex-wrap">
                      <div>
                        <p className="font-semibold text-gray-900">{r.masterName}</p>
                        <p className="text-sm text-gray-500 mt-0.5">{r.bookingsCount} услуг · комиссия {r.commissionPercent}%</p>
                      </div>
                      <div className="text-right">
                        <p className="text-sm text-gray-500">Итого: <span className="font-semibold text-gray-900">{r.totalAmount.toLocaleString('ru-RU')} ₽</span></p>
                        <p className="text-sm text-green-600">Мастеру: {r.masterEarnings.toLocaleString('ru-RU')} ₽</p>
                        <p className="text-sm text-primary-600">Компании: {r.companyEarnings.toLocaleString('ru-RU')} ₽</p>
                      </div>
                    </div>
                  </Card>
                ))}
              </div>
              <Card className="p-4 bg-orange-50">
                <div className="flex justify-between font-semibold text-gray-900 flex-wrap gap-2">
                  <span>Итого за период</span>
                  <div className="text-right">
                    <p>{data.reduce((s,r)=>s+r.totalAmount,0).toLocaleString('ru-RU')} ₽ всего</p>
                    <p className="text-green-600 text-sm">{data.reduce((s,r)=>s+r.masterEarnings,0).toLocaleString('ru-RU')} ₽ мастерам</p>
                    <p className="text-primary-600 text-sm">{data.reduce((s,r)=>s+r.companyEarnings,0).toLocaleString('ru-RU')} ₽ компании</p>
                  </div>
                </div>
              </Card>
            </>
          )}
        </>
      ) : (
        <p className="text-center text-gray-400 py-12">Задайте период и нажмите «Построить отчёт»</p>
      )}
    </div>
  )
}

// ── Main ──────────────────────────────────────────────────────────────────────

export function CabinetPage() {
  const { hasRole } = useAuthStore()
  const isOwner = hasRole('CompanyOwner') || hasRole('SuperAdmin')
  const isMaster = hasRole('Master') || hasRole('CompanyOwner') || hasRole('SuperAdmin')

  // Reports and Mailing are tariff-gated on the backend (402 without AllowAnalytics/AllowMailing),
  // with no visible feedback in the UI — so hide those tabs instead of letting an owner on a plan
  // without the feature open a page that silently does nothing.
  const { data: companiesForTabs } = useQuery({ queryKey: ['my-companies'], queryFn: companiesApi.getMy, enabled: isOwner })
  const hasAnalytics = !!companiesForTabs?.some(c => c.allowAnalytics)
  const hasMailing = !!companiesForTabs?.some(c => c.allowMailing)

  type Tab = 'dashboard' | 'companies' | 'schedule' | 'clients' | 'reports' | 'mailing'
  const tabs: { key: Tab; label: string; show: boolean }[] = [
    { key: 'dashboard', label: '📊 Дашборд',       show: isOwner },
    { key: 'companies', label: '🏢 Мои компании', show: isOwner },
    { key: 'schedule',  label: '📅 Расписание',   show: isMaster },
    { key: 'clients',   label: '👥 Клиенты',       show: isMaster },
    { key: 'reports',   label: '📋 Отчёты',        show: isOwner && hasAnalytics },
    { key: 'mailing',   label: '📣 Рассылка',      show: isOwner && hasMailing },
  ]
  const visible = tabs.filter(t => t.show)
  const [tab, setTab] = useState<Tab>(visible[0]?.key ?? 'schedule')

  return (
    <div className="max-w-4xl mx-auto px-4 py-8">
      <h1 className="text-2xl font-bold text-gray-900 mb-6">Кабинет</h1>
      <div className="flex gap-1 bg-gray-100 p-1 rounded-2xl mb-6 w-fit flex-wrap">
        {visible.map(t => (
          <button key={t.key} onClick={() => setTab(t.key)}
            className={`px-5 py-2 rounded-xl text-sm font-medium transition-all ${tab === t.key ? 'bg-white text-gray-900 shadow-sm' : 'text-gray-500 hover:text-gray-700'}`}>
            {t.label}
          </button>
        ))}
      </div>
      {tab === 'dashboard' && <DashboardSection />}
      {tab === 'companies' && <MyCompaniesTab />}
      {tab === 'schedule'  && <ScheduleSection />}
      {tab === 'clients'   && <ClientsSection />}
      {tab === 'reports'   && <ReportsTab />}
      {tab === 'mailing'   && <MailingSection />}
    </div>
  )
}
