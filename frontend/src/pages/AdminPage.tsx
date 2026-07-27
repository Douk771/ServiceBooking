import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import { adminApi, type AdminUser, type AdminCompany } from '../api/admin'
import { plansApi } from '../api/plans'
import { PlansTab } from './admin/PlansTab'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { Input } from '../components/ui/Input'
import { StatusBadge } from '../components/ui/Badge'
import { Modal } from '../components/ui/Modal'

// ── Stats tab ─────────────────────────────────────────────────────────────────

function StatsTab() {
  const { data, isLoading } = useQuery({ queryKey: ['admin-stats'], queryFn: adminApi.getStats })

  if (isLoading) return <div className="grid grid-cols-2 md:grid-cols-4 gap-4">{Array.from({length:4}).map((_,i)=><div key={i} className="h-24 bg-gray-100 rounded-2xl animate-pulse"/>)}</div>

  const tiles = [
    { label: 'Компаний', value: data?.totalCompanies ?? 0, icon: '🏢' },
    { label: 'Пользователей', value: data?.totalUsers ?? 0, icon: '👥' },
    { label: 'Записей всего', value: data?.totalBookings ?? 0, icon: '📅' },
    { label: 'Выручка (завершённые)', value: `${(data?.totalRevenue ?? 0).toLocaleString('ru-RU')} ₽`, icon: '💰' },
  ]

  return (
    <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
      {tiles.map(t => (
        <Card key={t.label} className="p-5 text-center">
          <div className="text-3xl mb-2">{t.icon}</div>
          <div className="text-2xl font-bold text-gray-900">{t.value}</div>
          <div className="text-sm text-gray-500 mt-1">{t.label}</div>
        </Card>
      ))}
    </div>
  )
}

// ── Account subscription modal (owner-scoped) ───────────────────────────────────

interface OwnerSubscription {
  ownerUserId: string
  ownerEmail: string
  planConfigId?: string
  paidUntil?: string
  subscriptionActive: boolean
}

function SubscriptionModal({ owner, onClose }: { owner: OwnerSubscription; onClose: () => void }) {
  const qc = useQueryClient()
  const [planConfigId, setPlanConfigId] = useState(owner.planConfigId ?? '')
  const [paidUntil, setPaidUntil] = useState(owner.paidUntil ? owner.paidUntil.slice(0, 10) : '')
  const [isActive, setIsActive] = useState(owner.subscriptionActive)
  const [comment, setComment] = useState('')

  const { data: plans } = useQuery({ queryKey: ['admin-plans'], queryFn: plansApi.list })
  const activePlans = (plans ?? []).filter(p => p.isActive)

  const { data: history } = useQuery({
    queryKey: ['admin-subscription-history', owner.ownerUserId],
    queryFn: () => adminApi.getSubscriptionHistory(owner.ownerUserId),
  })

  const mut = useMutation({
    mutationFn: () => adminApi.updateSubscription(owner.ownerUserId, planConfigId || null, paidUntil || null, isActive, comment || undefined),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['admin-users'] })
      qc.invalidateQueries({ queryKey: ['admin-companies'] })
      qc.invalidateQueries({ queryKey: ['admin-subscription-history', owner.ownerUserId] })
      onClose()
    },
  })

  return (
    <Modal title={`Подписка аккаунта — ${owner.ownerEmail}`} onClose={onClose}>
      <div className="flex flex-col gap-4">
        <p className="text-xs text-gray-500 bg-amber-50 rounded-xl px-3 py-2">
          Тариф привязан к аккаунту владельца и покрывает <span className="font-medium">все его компании</span>.
        </p>
        <div>
          <label className="text-sm font-medium text-gray-700 block mb-1">Тарифный план</label>
          <select value={planConfigId} onChange={e => setPlanConfigId(e.target.value)}
            className="w-full rounded-xl border border-gray-200 px-3 py-2.5 text-sm outline-none focus:border-primary-400">
            <option value="">Free (без тарифа)</option>
            {activePlans.map(p => (
              <option key={p.id} value={p.id}>
                {p.name} {p.pricePerMonth > 0 ? `— ${p.pricePerMonth.toLocaleString('ru-RU')} ₽/мес` : ''}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label className="text-sm font-medium text-gray-700 block mb-1">Оплачено до</label>
          <input type="date" value={paidUntil} onChange={e => setPaidUntil(e.target.value)}
            className="w-full rounded-xl border border-gray-200 px-3 py-2.5 text-sm outline-none focus:border-primary-400" />
        </div>
        <label className="flex items-center gap-3 cursor-pointer">
          <input type="checkbox" checked={isActive} onChange={e => setIsActive(e.target.checked)} className="w-4 h-4 rounded accent-orange-500" />
          <span className="text-sm text-gray-700">Подписка активна</span>
        </label>
        <div>
          <label className="text-sm font-medium text-gray-700 block mb-1">Комментарий к изменению</label>
          <textarea value={comment} onChange={e => setComment(e.target.value)} rows={2}
            className="w-full rounded-xl border border-gray-200 px-3 py-2 text-sm outline-none focus:border-primary-400 resize-none"
            placeholder="Способ оплаты, счёт и т.д." />
        </div>
        <div className="flex gap-3 pt-1">
          <Button variant="secondary" className="flex-1" onClick={onClose}>Отмена</Button>
          <Button className="flex-1" loading={mut.isPending} onClick={() => mut.mutate()}>Сохранить</Button>
        </div>

        {history && history.length > 0 && (
          <div className="pt-3 border-t border-gray-100">
            <p className="text-xs font-medium text-gray-400 uppercase tracking-wide mb-2">История изменений</p>
            <div className="flex flex-col gap-2 max-h-48 overflow-y-auto">
              {history.map(h => (
                <div key={h.id} className="text-xs bg-gray-50 rounded-xl p-2.5">
                  <div className="flex items-center justify-between text-gray-500">
                    <span>{format(parseISO(h.changedAt), 'd MMM yyyy, HH:mm', { locale: ru })}</span>
                    <span>{h.changedByEmail}</span>
                  </div>
                  <div className="text-gray-700 mt-1">
                    {h.oldPlanName} → <span className="font-medium">{h.newPlanName}</span>
                    {h.newPaidUntil && <> · до {format(parseISO(h.newPaidUntil), 'd MMM yyyy', { locale: ru })}</>}
                    {!h.newIsActive && <span className="text-red-500"> · выключена</span>}
                  </div>
                  {h.comment && <div className="text-gray-500 mt-0.5 italic">{h.comment}</div>}
                </div>
              ))}
            </div>
          </div>
        )}
      </div>
    </Modal>
  )
}

function ChangeOwnerModal({ company, onClose }: { company: AdminCompany; onClose: () => void }) {
  const qc = useQueryClient()
  const [search, setSearch] = useState('')
  const [selectedUserId, setSelectedUserId] = useState('')

  const { data: users } = useQuery({
    queryKey: ['admin-users', search],
    queryFn: () => adminApi.getUsers(search || undefined),
  })
  const candidates = (users ?? []).filter(u => u.id !== company.ownerUserId)

  const mut = useMutation({
    mutationFn: () => adminApi.updateCompanyOwner(company.id, selectedUserId),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['admin-companies'] })
      qc.invalidateQueries({ queryKey: ['admin-users'] })
      onClose()
    },
  })

  return (
    <Modal title={`Сменить владельца — ${company.name}`} onClose={onClose}>
      <div className="flex flex-col gap-4">
        <p className="text-xs text-gray-500 bg-amber-50 rounded-xl px-3 py-2">
          Владелец определяет, чей тариф действует для компании. Текущий владелец: <span className="font-medium">{company.ownerEmail}</span>.
          Новый владелец получит роль CompanyOwner и доступ к управлению компанией; доступ текущего владельца не отзывается автоматически.
        </p>
        <Input placeholder="Поиск пользователя по email или имени..." value={search} onChange={e => setSearch(e.target.value)} />
        <div className="flex flex-col gap-1 max-h-56 overflow-y-auto">
          {candidates.map(u => (
            <label key={u.id} className={`flex items-center gap-3 px-3 py-2 rounded-xl border cursor-pointer transition-all ${selectedUserId === u.id ? 'bg-primary-50 border-primary-300' : 'border-gray-200 hover:border-gray-300'}`}>
              <input type="radio" name="newOwner" className="accent-orange-500" checked={selectedUserId === u.id} onChange={() => setSelectedUserId(u.id)} />
              <div>
                <p className="text-sm font-medium text-gray-900">{u.firstName} {u.lastName}</p>
                <p className="text-xs text-gray-500">{u.phone}{u.email ? ` · ${u.email}` : ''}</p>
              </div>
            </label>
          ))}
          {candidates.length === 0 && <p className="text-sm text-gray-400 text-center py-4">Пользователи не найдены</p>}
        </div>
        {mut.isError && <p className="text-sm text-red-500">Не удалось сменить владельца</p>}
        <div className="flex gap-3 pt-1">
          <Button variant="secondary" className="flex-1" onClick={onClose}>Отмена</Button>
          <Button className="flex-1" loading={mut.isPending} disabled={!selectedUserId} onClick={() => mut.mutate()}>Сменить владельца</Button>
        </div>
      </div>
    </Modal>
  )
}

function CompaniesTab() {
  const [search, setSearch] = useState('')
  const [changeOwnerFor, setChangeOwnerFor] = useState<AdminCompany | null>(null)
  const { data, isLoading } = useQuery({ queryKey: ['admin-companies', search], queryFn: () => adminApi.getCompanies(search || undefined) })

  return (
    <div>
      {changeOwnerFor && <ChangeOwnerModal company={changeOwnerFor} onClose={() => setChangeOwnerFor(null)} />}
      <div className="mb-4">
        <Input placeholder="Поиск по названию или email..." value={search} onChange={e => setSearch(e.target.value)} />
      </div>
      {isLoading ? (
        <div className="grid gap-3">{Array.from({length:4}).map((_,i)=><div key={i} className="h-16 bg-gray-100 rounded-2xl animate-pulse"/>)}</div>
      ) : (
        <div className="grid gap-3">
          {(data ?? []).map(c => (
            <Card key={c.id} className="p-4 flex items-center justify-between gap-4 flex-wrap">
              <div className="flex items-center gap-3">
                <div className="w-10 h-10 rounded-xl bg-primary-100 flex items-center justify-center text-primary-700 font-bold shrink-0">{c.name[0]}</div>
                <div>
                  <div className="flex items-center gap-2 flex-wrap">
                    <span className="font-medium text-gray-900">{c.name}</span>
                    <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${c.planConfigId ? 'bg-blue-50 text-blue-700' : 'bg-gray-100 text-gray-600'}`}>{c.planName}</span>
                    {!c.isActive && <span className="text-xs bg-red-50 text-red-600 px-2 py-0.5 rounded-full">Заблокирована</span>}
                    {c.paidUntil && <span className="text-xs text-gray-400">до {format(parseISO(c.paidUntil), 'd MMM yyyy', {locale:ru})}</span>}
                  </div>
                  <p className="text-xs text-gray-400 mt-0.5">👤 {c.ownerEmail} · 👥 {c.memberCount} сотр. · 📅 {c.bookingCount} записей</p>
                </div>
              </div>
              <Button variant="secondary" size="sm" onClick={() => setChangeOwnerFor(c)}>Сменить владельца</Button>
            </Card>
          ))}
        </div>
      )}
    </div>
  )
}

// ── Users tab ─────────────────────────────────────────────────────────────────

function RolesModal({ user, onClose }: { user: AdminUser; onClose: () => void }) {
  const qc = useQueryClient()
  const ALL_ROLES = ['Client', 'Master', 'CompanyOwner', 'SuperAdmin']
  const [selected, setSelected] = useState<Set<string>>(new Set(user.roles))

  const toggle = (r: string) => setSelected(prev => { const n = new Set(prev); n.has(r) ? n.delete(r) : n.add(r); return n })

  const mut = useMutation({
    mutationFn: () => adminApi.updateUserRoles(user.id, [...selected]),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['admin-users'] }); onClose() },
  })

  return (
    <Modal title={`Роли — ${user.firstName} ${user.lastName}`} onClose={onClose}>
      <div className="flex flex-col gap-4">
        <div className="flex flex-wrap gap-2">
          {ALL_ROLES.map(r => (
            <label key={r} className={`flex items-center gap-2 px-3 py-2 rounded-xl border cursor-pointer transition-all ${selected.has(r) ? 'bg-primary-50 border-primary-300 text-primary-800' : 'border-gray-200 text-gray-600'}`}>
              <input type="checkbox" className="sr-only" checked={selected.has(r)} onChange={() => toggle(r)} />
              <span className={`w-4 h-4 rounded border flex items-center justify-center shrink-0 ${selected.has(r) ? 'bg-primary-500 border-primary-500' : 'border-gray-300'}`}>
                {selected.has(r) && <span className="text-white text-[10px]">✓</span>}
              </span>
              {r}
            </label>
          ))}
        </div>
        <div className="flex gap-3">
          <Button variant="secondary" className="flex-1" onClick={onClose}>Отмена</Button>
          <Button className="flex-1" loading={mut.isPending} onClick={() => mut.mutate()}>Сохранить</Button>
        </div>
      </div>
    </Modal>
  )
}

function UsersTab() {
  const [search, setSearch] = useState('')
  const [editUser, setEditUser] = useState<AdminUser | null>(null)
  const [editSub, setEditSub] = useState<AdminUser | null>(null)
  const { data, isLoading } = useQuery({ queryKey: ['admin-users', search], queryFn: () => adminApi.getUsers(search || undefined) })

  return (
    <div>
      {editUser && <RolesModal user={editUser} onClose={() => setEditUser(null)} />}
      {editSub && (
        <SubscriptionModal
          owner={{
            ownerUserId: editSub.id,
            ownerEmail: editSub.email ?? editSub.phone,
            planConfigId: editSub.planConfigId,
            paidUntil: editSub.paidUntil,
            subscriptionActive: editSub.subscriptionActive,
          }}
          onClose={() => setEditSub(null)}
        />
      )}
      <div className="mb-4">
        <Input placeholder="Поиск по email, имени..." value={search} onChange={e => setSearch(e.target.value)} />
      </div>
      {isLoading ? (
        <div className="grid gap-3">{Array.from({length:5}).map((_,i)=><div key={i} className="h-14 bg-gray-100 rounded-2xl animate-pulse"/>)}</div>
      ) : (
        <div className="grid gap-2">
          {(data ?? []).map(u => (
            <Card key={u.id} className="p-3 flex items-center justify-between gap-3">
              <div className="flex items-center gap-3 min-w-0">
                <div className="w-9 h-9 rounded-full bg-primary-100 flex items-center justify-center text-primary-700 font-bold text-sm shrink-0">{u.firstName[0]}{u.lastName[0]}</div>
                <div className="min-w-0">
                  <div className="flex items-center gap-2 flex-wrap">
                    <span className="font-medium text-gray-900 text-sm">{u.firstName} {u.lastName}</span>
                    {u.roles.map(r => <span key={r} className="text-xs bg-gray-100 text-gray-600 px-1.5 py-0.5 rounded">{r}</span>)}
                    {u.ownedCompanyCount > 0 && (
                      <span className={`text-xs px-1.5 py-0.5 rounded font-medium ${u.planConfigId ? 'bg-blue-50 text-blue-700' : 'bg-gray-100 text-gray-500'}`}>
                        {u.planName} · {u.ownedCompanyCount} комп.
                      </span>
                    )}
                  </div>
                  <p className="text-xs text-gray-400 truncate">{u.phone}{u.email ? ` · ${u.email}` : ''}</p>
                </div>
              </div>
              <div className="flex gap-2 shrink-0">
                {u.ownedCompanyCount > 0 && (
                  <Button size="sm" variant="secondary" onClick={() => setEditSub(u)}>Подписка</Button>
                )}
                <Button size="sm" variant="secondary" onClick={() => setEditUser(u)}>Роли</Button>
              </div>
            </Card>
          ))}
        </div>
      )}
    </div>
  )
}

// ── Bookings tab ──────────────────────────────────────────────────────────────

function AllBookingsTab() {
  const [from, setFrom] = useState(format(new Date(), 'yyyy-MM-dd'))
  const [to, setTo] = useState(format(new Date(), 'yyyy-MM-dd'))
  const [status, setStatus] = useState('')

  const { data, isLoading, refetch } = useQuery({
    queryKey: ['admin-bookings', from, to, status],
    queryFn: () => adminApi.getBookings({ from, to, status: status || undefined }),
    enabled: false,
  })

  return (
    <div>
      <div className="flex flex-wrap gap-3 mb-5">
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-gray-500">С</label>
          <input type="date" value={from} onChange={e => setFrom(e.target.value)} className="rounded-xl border border-gray-200 px-3 py-2 text-sm outline-none focus:border-primary-400" />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-gray-500">По</label>
          <input type="date" value={to} onChange={e => setTo(e.target.value)} className="rounded-xl border border-gray-200 px-3 py-2 text-sm outline-none focus:border-primary-400" />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-gray-500">Статус</label>
          <select value={status} onChange={e => setStatus(e.target.value)} className="rounded-xl border border-gray-200 px-3 py-2 text-sm outline-none focus:border-primary-400">
            <option value="">Все</option>
            <option value="Confirmed">Подтверждено</option>
            <option value="Completed">Выполнено</option>
            <option value="Cancelled">Отменено</option>
            <option value="NoShow">Не пришёл</option>
          </select>
        </div>
        <div className="flex items-end">
          <Button onClick={() => refetch()}>Найти</Button>
        </div>
      </div>

      {isLoading ? (
        <div className="grid gap-2">{Array.from({length:5}).map((_,i)=><div key={i} className="h-14 bg-gray-100 rounded-2xl animate-pulse"/>)}</div>
      ) : data ? (
        <div className="grid gap-2">
          {data.map(b => (
            <Card key={b.id} className="p-3 flex items-center justify-between gap-3 flex-wrap">
              <div>
                <div className="flex items-center gap-2 flex-wrap">
                  <span className="font-medium text-gray-900 text-sm">{b.clientName}</span>
                  <StatusBadge status={b.status} />
                  <span className="text-xs text-gray-400">{b.companyName}</span>
                </div>
                <p className="text-xs text-gray-500 mt-0.5">{b.serviceName} · {b.masterName} · {b.date} {b.startTime.slice(0,5)}</p>
                {b.clientPhone && <p className="text-xs text-gray-400">📞 {b.clientPhone}</p>}
              </div>
              <span className="text-sm font-semibold text-primary-600">{b.price.toLocaleString('ru-RU')} ₽</span>
            </Card>
          ))}
          {data.length === 0 && <p className="text-center text-gray-400 py-8">Записей не найдено</p>}
        </div>
      ) : (
        <p className="text-center text-gray-400 py-12">Задайте фильтры и нажмите «Найти»</p>
      )}
    </div>
  )
}

// ── Main ──────────────────────────────────────────────────────────────────────

type Tab = 'stats' | 'companies' | 'users' | 'bookings' | 'plans'

export function AdminPage() {
  const [tab, setTab] = useState<Tab>('stats')

  const tabs: { key: Tab; label: string }[] = [
    { key: 'stats', label: '📊 Дашборд' },
    { key: 'companies', label: '🏢 Компании' },
    { key: 'users', label: '👥 Пользователи' },
    { key: 'bookings', label: '📅 Записи' },
    { key: 'plans', label: '📋 Тарифы' },
  ]

  return (
    <div className="max-w-5xl mx-auto px-4 py-8">
      <h1 className="text-2xl font-bold text-gray-900 mb-6">Суперадминка</h1>
      <div className="flex gap-1 bg-gray-100 p-1 rounded-2xl mb-6 w-fit flex-wrap">
        {tabs.map(t => (
          <button key={t.key} onClick={() => setTab(t.key)}
            className={`px-4 py-2 rounded-xl text-sm font-medium transition-all ${tab === t.key ? 'bg-white text-gray-900 shadow-sm' : 'text-gray-500 hover:text-gray-700'}`}>
            {t.label}
          </button>
        ))}
      </div>
      {tab === 'stats' && <StatsTab />}
      {tab === 'companies' && <CompaniesTab />}
      {tab === 'users' && <UsersTab />}
      {tab === 'bookings' && <AllBookingsTab />}
      {tab === 'plans' && <PlansTab />}
    </div>
  )
}
