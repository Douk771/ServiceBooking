import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { format } from 'date-fns'
import { companiesApi } from '../api/companies'
import { CreateCompanyDialog } from '../components/company/CreateCompanyDialog'
import { ErrorBoundary } from '../components/ErrorBoundary'
import { adminApi } from '../api/admin'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { Icon } from '../components/ui/Icon'
import { ScheduleTab } from './owner/ScheduleTab'
import { DashboardTab } from './owner/DashboardTab'
import { MailingTab } from './owner/MailingTab'
import { NumbersBlock } from '../components/notifications/NumbersBlock'
import { MasterClientsPage } from './MasterClientsPage'
import { notificationChannelsApi } from '../api/notificationChannels'
import { ChannelBreachBanner } from '../components/notifications/ChannelBreachBanner'
import { GoodsShopsNotice } from '../components/company/GoodsShopsNotice'
import { getChannelBannerKind } from '../utils/channelBanner'
import { useAuthStore } from '../store/authStore'
import { formatRub } from '../utils/money'


function CompanyChips({
  companies,
  companyId,
  onSelect,
}: {
  companies: { id: string; name: string }[]
  companyId: string
  onSelect: (id: string) => void
}) {
  if (companies.length <= 1) return null
  return (
    <div className="flex flex-wrap gap-2 mb-4">
      {companies.map((c) => (
        <button
          key={c.id}
          onClick={() => onSelect(c.id)}
          className={`px-4 py-2 rounded-full border text-sm font-medium transition-all ${c.id === companyId ? 'bg-cream-deep border-line-strong text-ink' : 'bg-white border-line text-ink-soft hover:border-line-strong'}`}
        >
          {c.name}
        </button>
      ))}
    </div>
  )
}

// ── My companies (owner) ──────────────────────────────────────────────────────

export function MyCompaniesTab() {
  const [showCreate, setShowCreate] = useState(false)
  const { data: companies, isLoading } = useQuery({ queryKey: ['my-companies'], queryFn: companiesApi.getMy })

  return (
    <div>
      <div className="flex items-center justify-between mb-[18px]">
        <p className="text-sm text-ink-soft">Компании, которыми вы владеете</p>
        <Button onClick={() => setShowCreate(true)}>+ Создать компанию</Button>
      </div>

      {isLoading ? (
        <div className="flex flex-col gap-3">
          {Array.from({ length: 2 }).map((_, i) => (
            <div key={i} className="h-24 bg-cream-deep rounded-2xl animate-pulse" />
          ))}
        </div>
      ) : companies && companies.length > 0 ? (
        <div className="flex flex-col gap-3">
          {companies.map((c) => (
            <div
              key={c.id}
              className="bg-white border border-line rounded-[18px] px-[22px] py-[18px] flex items-center justify-between gap-3.5 flex-wrap"
            >
              <div className="flex items-center gap-3.5">
                <div className="w-[46px] h-[46px] rounded-[14px] bg-cream-deep flex items-center justify-center shrink-0">
                  <Icon name="store" size={20} strokeWidth={1.6} className="text-gold-dark" />
                </div>
                <div>
                  <p className="font-semibold text-[14.5px] text-ink mb-0.5">{c.name}</p>
                  <p className="text-[12.5px] text-muted">/{c.slug}</p>
                </div>
              </div>
              <div className="flex gap-2">
                <Link to={`/owner/company/${c.id}`}>
                  <Button variant="secondary" size="sm">
                    Управление
                  </Button>
                </Link>
                <Link
                  to={`/company/${c.slug}`}
                  target="_blank"
                  className="inline-flex items-center px-4 text-[13px] font-semibold text-ink-soft hover:text-ink"
                >
                  Открыть →
                </Link>
              </div>
            </div>
          ))}
        </div>
      ) : (
        <Card className="p-12 text-center text-muted">
          <Icon name="store" size={36} strokeWidth={1.4} className="mx-auto mb-3" />
          <p className="text-lg font-medium text-ink-soft">У вас ещё нет компаний</p>
          <Button className="mt-4" onClick={() => setShowCreate(true)}>
            Создать компанию
          </Button>
        </Card>
      )}

      {showCreate && <CreateCompanyDialog onClose={() => setShowCreate(false)} />}
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

  if (isLoading) return <div className="h-40 bg-cream-deep rounded-2xl animate-pulse" />
  if (!companies?.length)
    return (
      <Card className="p-12 text-center text-muted">
        <Icon name="store" size={32} strokeWidth={1.4} className="mx-auto mb-2" />
        <p>Вы не привязаны ни к одной компании</p>
      </Card>
    )

  return (
    <div>
      <CompanyChips companies={companies} companyId={companyId} onSelect={setSelectedId} />
      {companyId && <ScheduleTab companyId={companyId} selfMasterId={isOwner ? undefined : user?.id} />}
    </div>
  )
}

// ── Clients (master/owner) ────────────────────────────────────────────────────

function ClientsSection() {
  const { data: companies, isLoading } = useQuery({ queryKey: ['member-companies'], queryFn: companiesApi.getMemberOf })
  const [selectedId, setSelectedId] = useState('')
  const companyId = selectedId || companies?.[0]?.id || ''

  if (isLoading) return <div className="h-40 bg-cream-deep rounded-2xl animate-pulse" />
  if (!companies?.length)
    return (
      <Card className="p-12 text-center text-muted">
        <Icon name="store" size={32} strokeWidth={1.4} className="mx-auto mb-2" />
        <p>Вы не привязаны ни к одной компании</p>
      </Card>
    )

  return (
    <div>
      <CompanyChips companies={companies} companyId={companyId} onSelect={setSelectedId} />
      {companyId && <MasterClientsPage companyId={companyId} />}
    </div>
  )
}

// ── Dashboard (owner) ─────────────────────────────────────────────────────────

function DashboardSection() {
  const { data: companies, isLoading } = useQuery({ queryKey: ['my-companies'], queryFn: companiesApi.getMy })

  if (isLoading) return <div className="h-40 bg-cream-deep rounded-2xl animate-pulse" />
  if (!companies?.length)
    return (
      <Card className="p-12 text-center text-muted">
        <Icon name="store" size={32} strokeWidth={1.4} className="mx-auto mb-2" />
        <p>У вас нет компаний</p>
      </Card>
    )

  return <DashboardTab companies={companies} />
}

// ── Mailing (owner) ───────────────────────────────────────────────────────────

function MailingSection() {
  const { data: companies, isLoading } = useQuery({ queryKey: ['my-companies'], queryFn: companiesApi.getMy })
  const [selectedId, setSelectedId] = useState('')
  const companyId = selectedId || companies?.[0]?.id || ''

  if (isLoading) return <div className="h-40 bg-cream-deep rounded-2xl animate-pulse" />
  if (!companies?.length)
    return (
      <Card className="p-12 text-center text-muted">
        <Icon name="store" size={32} strokeWidth={1.4} className="mx-auto mb-2" />
        <p>У вас нет компаний</p>
      </Card>
    )

  return (
    <div>
      <CompanyChips companies={companies} companyId={companyId} onSelect={setSelectedId} />
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
      <div className="flex flex-wrap gap-3 mb-5 items-end">
        {companies && companies.length > 1 && (
          <select
            value={selectedId}
            onChange={(e) => setCompanyId(e.target.value)}
            className="rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold bg-white text-ink"
          >
            {companies.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        )}
        <div className="flex flex-col gap-1">
          <label className="text-xs text-muted">С</label>
          <input
            type="date"
            value={from}
            onChange={(e) => setFrom(e.target.value)}
            className="rounded-xl border border-line px-3.5 py-2.5 text-[13.5px] outline-none focus:border-gold bg-white text-ink"
          />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs text-muted">По</label>
          <input
            type="date"
            value={to}
            onChange={(e) => setTo(e.target.value)}
            className="rounded-xl border border-line px-3.5 py-2.5 text-[13.5px] outline-none focus:border-gold bg-white text-ink"
          />
        </div>
        <Button onClick={() => refetch()} disabled={!selectedId}>
          Построить отчёт
        </Button>
      </div>

      {isLoading ? (
        <div className="flex flex-col gap-2.5">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="h-20 bg-cream-deep rounded-2xl animate-pulse" />
          ))}
        </div>
      ) : data ? (
        <>
          {data.length === 0 ? (
            <p className="text-center text-muted py-8">Нет завершённых записей за период</p>
          ) : (
            <>
              <div className="flex flex-col gap-2.5 mb-3.5">
                {data.map((r) => (
                  <div
                    key={r.masterId}
                    className="bg-white border border-line rounded-2xl px-5 py-4 flex items-center justify-between gap-4 flex-wrap"
                  >
                    <div>
                      <p className="font-semibold text-sm text-ink">{r.masterName}</p>
                      <p className="text-[12.5px] text-muted mt-0.5">
                        {r.bookingsCount} услуг · комиссия {r.commissionPercent}%
                      </p>
                    </div>
                    <div className="text-right text-[13px]">
                      <p className="text-ink-soft">
                        Итого: <strong className="text-ink">{formatRub(r.totalAmount)}</strong>
                      </p>
                      <p className="text-success mt-0.5">Мастеру: {formatRub(r.masterEarnings)}</p>
                    </div>
                  </div>
                ))}
              </div>
              <div className="bg-cream-deep rounded-2xl px-5 py-4 flex justify-between font-semibold text-ink flex-wrap gap-2">
                <span>Итого за период</span>
                <div className="text-right text-[13px]">
                  <p className="text-ink font-semibold">
                    {formatRub(data.reduce((s, r) => s + r.totalAmount, 0))} всего
                  </p>
                  <p className="text-success">
                    {formatRub(data.reduce((s, r) => s + r.masterEarnings, 0))} мастерам
                  </p>
                </div>
              </div>
            </>
          )}
        </>
      ) : (
        <p className="text-center text-muted py-12">Задайте период и нажмите «Построить отчёт»</p>
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
  const { data: companiesForTabs } = useQuery({
    queryKey: ['my-companies'],
    queryFn: companiesApi.getMy,
    enabled: isOwner,
  })
  const hasAnalytics = !!companiesForTabs?.some((c) => c.allowAnalytics)
  const hasMailing = !!companiesForTabs?.some((c) => c.allowMailing)

  // US-62 п. 2 — the cabinet header is one of the three places the breach banner must live, so it
  // shows regardless of which tab is open, not just inside the Notifications tab.
  const { data: ownerChannels } = useQuery({
    queryKey: ['notification-channels'],
    queryFn: notificationChannelsApi.list,
    enabled: isOwner,
  })
  const brokenChannels = (ownerChannels ?? []).filter((c) => getChannelBannerKind(c.state, c.idleDeadline))

  type Tab = 'dashboard' | 'companies' | 'schedule' | 'clients' | 'reports' | 'mailing' | 'notifications'
  const tabs: { key: Tab; label: string; show: boolean }[] = [
    { key: 'dashboard', label: 'Дашборд', show: isOwner },
    { key: 'companies', label: 'Мои компании', show: isOwner },
    { key: 'schedule', label: 'Расписание', show: isMaster },
    { key: 'clients', label: 'Клиенты', show: isMaster },
    { key: 'reports', label: 'Отчёты', show: isOwner && hasAnalytics },
    { key: 'mailing', label: 'Рассылка', show: isOwner && hasMailing },
    // Not gated the way Reports/Mailing are: this tab must show even before a channel exists — it's
    // where the owner discovers and buys the option in the first place (US-53 п. 1).
    { key: 'notifications', label: 'Уведомления', show: isOwner },
  ]
  const visible = tabs.filter((t) => t.show)
  const [tab, setTab] = useState<Tab>(visible[0]?.key ?? 'schedule')

  return (
    <div className="max-w-[1080px] mx-auto px-8 pt-11 pb-24">
      <h1 className="font-serif text-[30px] font-medium text-ink mb-6">Кабинет</h1>
      <GoodsShopsNotice />
      {brokenChannels.length > 0 && (
        <div className="flex flex-col gap-3 mb-6">
          {brokenChannels.map((c) => (
            <ChannelBreachBanner
              key={c.id}
              state={c.state}
              stateText={c.stateText}
              idleSince={c.idleSince}
              idleDeadline={c.idleDeadline}
            />
          ))}
        </div>
      )}
      <div className="flex gap-1 bg-cream-deep p-1 rounded-full mb-8 w-fit flex-wrap">
        {visible.map((t) => (
          <button
            key={t.key}
            onClick={() => setTab(t.key)}
            className={`px-4 py-[9px] rounded-full text-[13.5px] font-semibold whitespace-nowrap transition-all ${tab === t.key ? 'bg-white text-ink shadow-sm' : 'text-gold-dark hover:text-ink'}`}
          >
            {t.label}
          </button>
        ))}
      </div>
      {/* Same reasoning as AdminPage: one boundary per tab, keyed on the tab, so a crash in one
          tab's content doesn't take the tab bar or the breach banner above it down too (§103.1). */}
      <ErrorBoundary key={tab} label={visible.find((t) => t.key === tab)?.label}>
        {tab === 'dashboard' && <DashboardSection />}
        {tab === 'companies' && <MyCompaniesTab />}
        {tab === 'schedule' && <ScheduleSection />}
        {tab === 'clients' && <ClientsSection />}
        {tab === 'reports' && <ReportsTab />}
        {tab === 'mailing' && <MailingSection />}
        {tab === 'notifications' && <NumbersBlock />}
      </ErrorBoundary>
    </div>
  )
}
