import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import { adminApi, type AdminUser, type AdminCompany } from '../api/admin'
import { plansApi } from '../api/plans'
import { PlansTab } from './admin/PlansTab'
import { NotificationsAdminTab } from './admin/NotificationsAdminTab'
import { SubjectRequestsTab } from './admin/SubjectRequestsTab'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { Input } from '../components/ui/Input'
import { StatusBadge } from '../components/ui/Badge'
import { Modal } from '../components/ui/Modal'
import { Icon } from '../components/ui/Icon'
import { Pagination } from '../components/ui/Pagination'
import { getPlanErrorMessage } from '../utils/planError'
import { getCompanyAdminErrorMessage } from '../utils/companyAdminError'
import { formatPhone } from '../utils/phone'

// ── Stats tab ─────────────────────────────────────────────────────────────────

function StatsTab() {
  const { data, isLoading } = useQuery({ queryKey: ['admin-stats'], queryFn: adminApi.getStats })

  if (isLoading)
    return (
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        {Array.from({ length: 4 }).map((_, i) => (
          <div key={i} className="h-24 bg-cream-deep rounded-2xl animate-pulse" />
        ))}
      </div>
    )

  const tiles = [
    { label: 'Компаний', value: data?.totalCompanies ?? 0 },
    { label: 'Пользователей', value: data?.totalUsers ?? 0 },
    { label: 'Записей всего', value: data?.totalBookings ?? 0 },
    { label: 'Выручка (завершённые)', value: `${(data?.totalRevenue ?? 0).toLocaleString('ru-RU')} ₽` },
  ]

  return (
    <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
      {tiles.map((t) => (
        <Card key={t.label} className="p-6 text-center">
          <p className="text-[26px] font-bold text-ink mb-1.5">{t.value}</p>
          <p className="text-[13px] text-ink-soft">{t.label}</p>
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
  const activePlans = (plans ?? []).filter((p) => p.isActive)
  // The owner's current plan may have been deactivated after assignment (US-08). It stays selected
  // and visible, marked "(неактивен)", so saving without touching the dropdown doesn't silently drop
  // the owner to Free — but it isn't offered to switch a different owner onto it.
  const currentPlan = plans?.find((p) => p.id === owner.planConfigId)
  const currentPlanInactive = !!currentPlan && !currentPlan.isActive

  const { data: history } = useQuery({
    queryKey: ['admin-subscription-history', owner.ownerUserId],
    queryFn: () => adminApi.getSubscriptionHistory(owner.ownerUserId),
  })

  const mut = useMutation({
    mutationFn: () =>
      adminApi.updateSubscription(
        owner.ownerUserId,
        planConfigId || null,
        paidUntil || null,
        isActive,
        comment || undefined,
      ),
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
        <p className="text-xs text-muted bg-warning-bg rounded-xl px-3 py-2">
          Тариф привязан к аккаунту владельца и покрывает <span className="font-medium">все его компании</span>.
        </p>
        <div>
          <label className="text-sm font-medium text-ink-soft block mb-1">Тарифный план</label>
          <select
            value={planConfigId}
            onChange={(e) => setPlanConfigId(e.target.value)}
            className="w-full rounded-xl border border-line px-3 py-2.5 text-sm outline-none focus:border-gold"
          >
            <option value="">Free (без тарифа)</option>
            {currentPlanInactive && <option value={currentPlan!.id}>{currentPlan!.name} (неактивен)</option>}
            {activePlans.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name} {p.pricePerMonth > 0 ? `— ${p.pricePerMonth.toLocaleString('ru-RU')} ₽/мес` : ''}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label className="text-sm font-medium text-ink-soft block mb-1">Оплачено до</label>
          <input
            type="date"
            value={paidUntil}
            onChange={(e) => setPaidUntil(e.target.value)}
            className="w-full rounded-xl border border-line px-3 py-2.5 text-sm outline-none focus:border-gold"
          />
        </div>
        <label className="flex items-center gap-3 cursor-pointer">
          <input
            type="checkbox"
            checked={isActive}
            onChange={(e) => setIsActive(e.target.checked)}
            className="w-4 h-4 rounded accent-gold"
          />
          <span className="text-sm text-ink-soft">Подписка активна</span>
        </label>
        <div>
          <label className="text-sm font-medium text-ink-soft block mb-1">Комментарий к изменению</label>
          <textarea
            value={comment}
            onChange={(e) => setComment(e.target.value)}
            rows={2}
            className="w-full rounded-xl border border-line px-3 py-2 text-sm outline-none focus:border-gold resize-none"
            placeholder="Способ оплаты, счёт и т.д."
          />
        </div>
        {mut.isError && (
          <p className="text-sm text-danger">{getPlanErrorMessage(mut.error, 'Не удалось сохранить подписку.')}</p>
        )}

        <div className="flex gap-3 pt-1">
          <Button variant="secondary" className="flex-1" onClick={onClose}>
            Отмена
          </Button>
          <Button className="flex-1" loading={mut.isPending} onClick={() => mut.mutate()}>
            Сохранить
          </Button>
        </div>

        {history && history.length > 0 && (
          <div className="pt-3 border-t border-line">
            <p className="text-xs font-medium text-muted uppercase tracking-wide mb-2">История изменений</p>
            <div className="flex flex-col gap-2 max-h-48 overflow-y-auto">
              {history.map((h) => (
                <div key={h.id} className="text-xs bg-cream-deep rounded-xl p-2.5">
                  <div className="flex items-center justify-between text-muted">
                    <span>{format(parseISO(h.changedAt), 'd MMM yyyy, HH:mm', { locale: ru })}</span>
                    <span>{h.changedByEmail}</span>
                  </div>
                  <div className="text-ink-soft mt-1">
                    {h.oldPlanName} → <span className="font-medium">{h.newPlanName}</span>
                    {h.newPaidUntil && <> · до {format(parseISO(h.newPaidUntil), 'd MMM yyyy', { locale: ru })}</>}
                    {!h.newIsActive && <span className="text-danger"> · выключена</span>}
                  </div>
                  {h.comment && <div className="text-muted mt-0.5 italic">{h.comment}</div>}
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
  const candidates = (users?.items ?? []).filter((u) => u.id !== company.ownerUserId)

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
        <p className="text-xs text-muted bg-warning-bg rounded-xl px-3 py-2">
          Владелец определяет, чей тариф действует для компании. Текущий владелец:{' '}
          <span className="font-medium">{company.ownerEmail}</span>. Новый владелец получит роль CompanyOwner и доступ к
          управлению компанией; доступ текущего владельца не отзывается автоматически.
        </p>
        <Input
          placeholder="Поиск пользователя по email или имени..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <div className="flex flex-col gap-1 max-h-56 overflow-y-auto">
          {candidates.map((u) => (
            <label
              key={u.id}
              className={`flex items-center gap-3 px-3 py-2 rounded-xl border cursor-pointer transition-all ${selectedUserId === u.id ? 'bg-cream-deep border-line-strong' : 'border-line hover:border-line-strong'}`}
            >
              <input
                type="radio"
                name="newOwner"
                className="accent-gold"
                checked={selectedUserId === u.id}
                onChange={() => setSelectedUserId(u.id)}
              />
              <div>
                <p className="text-sm font-medium text-ink">
                  {u.firstName} {u.lastName}
                </p>
                <p className="text-xs text-muted">
                  {formatPhone(u.phone)}
                  {u.email ? ` · ${u.email}` : ''}
                </p>
              </div>
            </label>
          ))}
          {candidates.length === 0 && <p className="text-sm text-muted text-center py-4">Пользователи не найдены</p>}
        </div>
        {mut.isError && <p className="text-sm text-danger">Не удалось сменить владельца</p>}
        <div className="flex gap-3 pt-1">
          <Button variant="secondary" className="flex-1" onClick={onClose}>
            Отмена
          </Button>
          <Button className="flex-1" loading={mut.isPending} disabled={!selectedUserId} onClick={() => mut.mutate()}>
            Сменить владельца
          </Button>
        </div>
      </div>
    </Modal>
  )
}

function BlockCompanyModal({ company, onClose }: { company: AdminCompany; onClose: () => void }) {
  const qc = useQueryClient()
  const willBlock = company.isActive

  const mut = useMutation({
    // AdminUpdateCompanyDto requires all three fields — sending only isActive would silently reset
    // the others to their zero values (API_CONTRACT.md §14), so the currently-known name and
    // allowSelfBooking travel along even though this screen only changes isActive.
    mutationFn: () =>
      adminApi.updateCompany(company.id, {
        name: company.name,
        isActive: !company.isActive,
        allowSelfBooking: company.allowSelfBooking,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['admin-companies'] })
      onClose()
    },
  })

  return (
    <Modal
      title={willBlock ? `Заблокировать «${company.name}»?` : `Разблокировать «${company.name}»?`}
      onClose={onClose}
    >
      <div className="flex flex-col gap-4">
        {willBlock ? (
          <div className="text-sm text-ink-soft flex flex-col gap-1.5">
            <p>После блокировки:</p>
            <ul className="list-disc pl-5 flex flex-col gap-1">
              <li>компания пропадёт из публичного каталога;</li>
              <li>её страница перестанет открываться;</li>
              <li>сотрудники не увидят её в своём кабинете.</li>
            </ul>
            <p className="mt-1">
              Уже созданные записи <strong>не отменяются</strong>, роли сотрудников не отзываются.
            </p>
          </div>
        ) : (
          <p className="text-sm text-ink-soft">
            Компания снова появится в каталоге, её страница и кабинет сотрудников станут доступны.
          </p>
        )}
        {mut.isError && <p className="text-sm text-danger">{getCompanyAdminErrorMessage(mut.error)}</p>}
        <div className="flex gap-3 pt-1">
          <Button variant="secondary" className="flex-1" onClick={onClose} disabled={mut.isPending}>
            Отмена
          </Button>
          <Button
            variant={willBlock ? 'danger' : 'primary'}
            className="flex-1"
            loading={mut.isPending}
            onClick={() => mut.mutate()}
          >
            {willBlock ? 'Заблокировать' : 'Разблокировать'}
          </Button>
        </div>
      </div>
    </Modal>
  )
}

function CompaniesTab() {
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [changeOwnerFor, setChangeOwnerFor] = useState<AdminCompany | null>(null)
  const [blockingCompany, setBlockingCompany] = useState<AdminCompany | null>(null)
  const { data, isLoading } = useQuery({
    queryKey: ['admin-companies', search, page],
    queryFn: () => adminApi.getCompanies(search || undefined, page),
  })
  // Typing a new search always restarts at page 1 — otherwise "page 3" of the old, wider result set
  // could be past the end of a narrower one and render nothing with no indication why.
  const handleSearch = (value: string) => {
    setSearch(value)
    setPage(1)
  }

  return (
    <div>
      {changeOwnerFor && <ChangeOwnerModal company={changeOwnerFor} onClose={() => setChangeOwnerFor(null)} />}
      {blockingCompany && <BlockCompanyModal company={blockingCompany} onClose={() => setBlockingCompany(null)} />}
      <div className="mb-4">
        <Input
          placeholder="Поиск по названию или email..."
          value={search}
          onChange={(e) => handleSearch(e.target.value)}
        />
      </div>
      {isLoading ? (
        <div className="grid gap-3">
          {Array.from({ length: 4 }).map((_, i) => (
            <div key={i} className="h-16 bg-cream-deep rounded-2xl animate-pulse" />
          ))}
        </div>
      ) : (
        <div className="grid gap-3">
          {(data?.items ?? []).map((c) => (
            <Card key={c.id} className="p-4 flex items-center justify-between gap-4 flex-wrap">
              <div className="flex items-center gap-3">
                <div className="w-10 h-10 rounded-xl bg-cream-deep flex items-center justify-center text-gold-dark font-bold shrink-0">
                  {c.name[0]}
                </div>
                <div>
                  <div className="flex items-center gap-2 flex-wrap">
                    <span className="font-medium text-ink">{c.name}</span>
                    <span
                      className={`text-xs px-2 py-0.5 rounded-full font-medium ${c.planConfigId ? 'bg-info-bg text-info' : 'bg-cream-deep text-ink-soft'}`}
                    >
                      {c.planName}
                    </span>
                    {!c.isActive && (
                      <span className="text-xs bg-danger-bg text-danger px-2 py-0.5 rounded-full">Заблокирована</span>
                    )}
                    {c.paidUntil && (
                      <span className="text-xs text-muted">
                        до {format(parseISO(c.paidUntil), 'd MMM yyyy', { locale: ru })}
                      </span>
                    )}
                  </div>
                  <p className="text-xs text-muted mt-0.5">
                    {c.ownerEmail} · {c.memberCount} сотр. · {c.bookingCount} записей
                  </p>
                </div>
              </div>
              <div className="flex gap-2">
                <Button variant={c.isActive ? 'danger' : 'secondary'} size="sm" onClick={() => setBlockingCompany(c)}>
                  {c.isActive ? 'Заблокировать' : 'Разблокировать'}
                </Button>
                <Button variant="secondary" size="sm" onClick={() => setChangeOwnerFor(c)}>
                  Сменить владельца
                </Button>
              </div>
            </Card>
          ))}
        </div>
      )}
      {data && (
        <Pagination
          page={data.page}
          pageSize={data.pageSize}
          total={data.total}
          hasNext={data.hasNext}
          onPageChange={setPage}
        />
      )}
    </div>
  )
}

// ── Users tab ─────────────────────────────────────────────────────────────────

function RolesModal({ user, onClose }: { user: AdminUser; onClose: () => void }) {
  const qc = useQueryClient()
  const ALL_ROLES = ['Client', 'Master', 'CompanyOwner', 'SuperAdmin']
  const [selected, setSelected] = useState<Set<string>>(new Set(user.roles))

  const toggle = (r: string) =>
    setSelected((prev) => {
      const n = new Set(prev)
      if (n.has(r)) n.delete(r)
      else n.add(r)
      return n
    })

  const mut = useMutation({
    mutationFn: () => adminApi.updateUserRoles(user.id, [...selected]),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['admin-users'] })
      onClose()
    },
  })

  return (
    <Modal title={`Роли — ${user.firstName} ${user.lastName}`} onClose={onClose}>
      <div className="flex flex-col gap-4">
        <div className="flex flex-wrap gap-2">
          {ALL_ROLES.map((r) => (
            <label
              key={r}
              className={`flex items-center gap-2 px-3 py-2 rounded-xl border cursor-pointer transition-all ${selected.has(r) ? 'bg-cream-deep border-line-strong text-gold-dark' : 'border-line text-ink-soft'}`}
            >
              <input type="checkbox" className="sr-only" checked={selected.has(r)} onChange={() => toggle(r)} />
              <span
                className={`w-4 h-4 rounded border flex items-center justify-center shrink-0 ${selected.has(r) ? 'bg-gold-dark border-gold-dark' : 'border-line-strong'}`}
              >
                {selected.has(r) && <span className="text-cream text-[10px]">✓</span>}
              </span>
              {r}
            </label>
          ))}
        </div>
        <div className="flex gap-3">
          <Button variant="secondary" className="flex-1" onClick={onClose}>
            Отмена
          </Button>
          <Button className="flex-1" loading={mut.isPending} onClick={() => mut.mutate()}>
            Сохранить
          </Button>
        </div>
      </div>
    </Modal>
  )
}

function UsersTab() {
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [editUser, setEditUser] = useState<AdminUser | null>(null)
  const [editSub, setEditSub] = useState<AdminUser | null>(null)
  const { data, isLoading } = useQuery({
    queryKey: ['admin-users', search, page],
    queryFn: () => adminApi.getUsers(search || undefined, page),
  })
  const handleSearch = (value: string) => {
    setSearch(value)
    setPage(1)
  }

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
        <Input placeholder="Поиск по email, имени..." value={search} onChange={(e) => handleSearch(e.target.value)} />
      </div>
      {isLoading ? (
        <div className="grid gap-3">
          {Array.from({ length: 5 }).map((_, i) => (
            <div key={i} className="h-14 bg-cream-deep rounded-2xl animate-pulse" />
          ))}
        </div>
      ) : (
        <div className="grid gap-2">
          {(data?.items ?? []).map((u) => (
            <Card key={u.id} className="p-3 flex items-center justify-between gap-3">
              <div className="flex items-center gap-3 min-w-0">
                <div className="w-9 h-9 rounded-full bg-cream-deep flex items-center justify-center text-gold-dark font-bold text-sm shrink-0">
                  {u.firstName[0]}
                  {u.lastName[0]}
                </div>
                <div className="min-w-0">
                  <div className="flex items-center gap-2 flex-wrap">
                    <span className="font-medium text-ink text-sm">
                      {u.firstName} {u.lastName}
                    </span>
                    {u.roles.map((r) => (
                      <span key={r} className="text-xs bg-cream-deep text-ink-soft px-1.5 py-0.5 rounded">
                        {r}
                      </span>
                    ))}
                    {u.ownedCompanyCount > 0 && (
                      <span
                        className={`text-xs px-1.5 py-0.5 rounded font-medium ${u.planConfigId ? 'bg-info-bg text-info' : 'bg-cream-deep text-muted'}`}
                      >
                        {u.planName} · {u.ownedCompanyCount} комп.
                      </span>
                    )}
                  </div>
                  <p className="text-xs text-muted truncate">
                    {formatPhone(u.phone)}
                    {u.email ? ` · ${u.email}` : ''}
                  </p>
                </div>
              </div>
              <div className="flex gap-2 shrink-0">
                {u.ownedCompanyCount > 0 && (
                  <Button size="sm" variant="secondary" onClick={() => setEditSub(u)}>
                    Подписка
                  </Button>
                )}
                <Button size="sm" variant="secondary" onClick={() => setEditUser(u)}>
                  Роли
                </Button>
              </div>
            </Card>
          ))}
        </div>
      )}
      {data && (
        <Pagination
          page={data.page}
          pageSize={data.pageSize}
          total={data.total}
          hasNext={data.hasNext}
          onPageChange={setPage}
        />
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
          <label className="text-xs font-medium text-muted">С</label>
          <input
            type="date"
            value={from}
            onChange={(e) => setFrom(e.target.value)}
            className="rounded-xl border border-line px-3 py-2 text-sm outline-none focus:border-gold"
          />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-muted">По</label>
          <input
            type="date"
            value={to}
            onChange={(e) => setTo(e.target.value)}
            className="rounded-xl border border-line px-3 py-2 text-sm outline-none focus:border-gold"
          />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-muted">Статус</label>
          <select
            value={status}
            onChange={(e) => setStatus(e.target.value)}
            className="rounded-xl border border-line px-3 py-2 text-sm outline-none focus:border-gold"
          >
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
        <div className="grid gap-2">
          {Array.from({ length: 5 }).map((_, i) => (
            <div key={i} className="h-14 bg-cream-deep rounded-2xl animate-pulse" />
          ))}
        </div>
      ) : data ? (
        <div className="grid gap-2">
          {data.map((b) => (
            <Card key={b.id} className="p-3 flex items-center justify-between gap-3 flex-wrap">
              <div>
                <div className="flex items-center gap-2 flex-wrap">
                  <span className="font-medium text-ink text-sm">{b.clientName}</span>
                  <StatusBadge status={b.status} />
                  <span className="text-xs text-muted">{b.companyName}</span>
                </div>
                <p className="text-xs text-muted mt-0.5">
                  {b.serviceName} · {b.masterName} · {b.date} {b.startTime.slice(0, 5)}
                </p>
                {b.clientPhone && (
                  <p className="text-xs text-muted flex items-center gap-1">
                    <Icon name="phone" size={11} strokeWidth={1.8} /> {formatPhone(b.clientPhone)}
                  </p>
                )}
              </div>
              <span className="text-sm font-semibold text-gold-dark">{b.price.toLocaleString('ru-RU')} ₽</span>
            </Card>
          ))}
          {data.length === 0 && <p className="text-center text-muted py-8">Записей не найдено</p>}
        </div>
      ) : (
        <p className="text-center text-muted py-12">Задайте фильтры и нажмите «Найти»</p>
      )}
    </div>
  )
}

// ── Main ──────────────────────────────────────────────────────────────────────

type Tab = 'stats' | 'companies' | 'users' | 'bookings' | 'plans' | 'notifications' | 'subject-requests'

export function AdminPage() {
  const [tab, setTab] = useState<Tab>('stats')

  const tabs: { key: Tab; label: string }[] = [
    { key: 'stats', label: 'Дашборд' },
    { key: 'companies', label: 'Компании' },
    { key: 'users', label: 'Пользователи' },
    { key: 'bookings', label: 'Записи' },
    { key: 'plans', label: 'Тарифы' },
    { key: 'notifications', label: 'Каналы уведомлений' },
    { key: 'subject-requests', label: 'Обращения субъектов' },
  ]

  return (
    <div className="max-w-[1080px] mx-auto px-8 pt-11 pb-24">
      <h1 className="font-serif text-[30px] font-medium text-ink mb-6">Суперадминка</h1>
      <div className="flex gap-1 bg-cream-deep p-1 rounded-full mb-8 w-fit flex-wrap">
        {tabs.map((t) => (
          <button
            key={t.key}
            onClick={() => setTab(t.key)}
            className={`px-4 py-[9px] rounded-full text-sm font-semibold transition-all whitespace-nowrap ${tab === t.key ? 'bg-white text-ink' : 'text-gold-dark hover:text-ink'}`}
          >
            {t.label}
          </button>
        ))}
      </div>
      {tab === 'stats' && <StatsTab />}
      {tab === 'companies' && <CompaniesTab />}
      {tab === 'users' && <UsersTab />}
      {tab === 'bookings' && <AllBookingsTab />}
      {tab === 'plans' && <PlansTab />}
      {tab === 'notifications' && <NotificationsAdminTab />}
      {tab === 'subject-requests' && <SubjectRequestsTab />}
    </div>
  )
}
