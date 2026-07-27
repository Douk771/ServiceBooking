import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { format, startOfDay, endOfDay, startOfWeek, endOfWeek, startOfMonth, endOfMonth } from 'date-fns'
import { statsApi, type CompanyStats } from '../../api/stats'
import { Card } from '../../components/ui/Card'
import { Icon } from '../../components/ui/Icon'

type Period = 'today' | 'week' | 'month' | 'custom'

interface Props {
  companies: { id: string; name: string }[]
}

function StatTile({ label, value }: { label: string; value: string }) {
  return (
    <div className="bg-white border border-line rounded-[18px] p-5">
      <p className="text-[12.5px] text-muted mb-2">{label}</p>
      <p className="text-[22px] font-bold text-ink">{value}</p>
    </div>
  )
}

function BarChart({ data }: { data: { date: string; revenue: number }[] }) {
  const max = Math.max(...data.map(d => d.revenue), 1)
  return (
    <div className="flex items-end gap-1 h-32 overflow-x-auto pb-1">
      {data.map(d => {
        const pct = (d.revenue / max) * 100
        const label = d.date.slice(5) // MM-DD
        return (
          <div key={d.date} className="flex flex-col items-center gap-1 shrink-0" style={{ minWidth: 28 }}>
            <div
              className="w-full bg-[#C9A877] rounded-t-md transition-all"
              style={{ height: `${Math.max(pct, 2)}%` }}
              title={`${d.revenue.toLocaleString('ru-RU')} ₽`}
            />
            <span className="text-[10px] text-muted rotate-45 origin-left">{label}</span>
          </div>
        )
      })}
    </div>
  )
}

export function DashboardTab({ companies }: Props) {
  const [companyId, setCompanyId] = useState(companies[0]?.id ?? '')
  const [period, setPeriod] = useState<Period>('week')
  const [customFrom, setCustomFrom] = useState(format(new Date(), 'yyyy-MM-dd'))
  const [customTo, setCustomTo] = useState(format(new Date(), 'yyyy-MM-dd'))

  const selectedId = companyId || companies[0]?.id || ''

  function getPeriodDates(): { from: string; to: string } {
    const now = new Date()
    if (period === 'today') return {
      from: format(startOfDay(now), 'yyyy-MM-dd'),
      to: format(endOfDay(now), 'yyyy-MM-dd'),
    }
    if (period === 'week') return {
      from: format(startOfWeek(now, { weekStartsOn: 1 }), 'yyyy-MM-dd'),
      to: format(endOfWeek(now, { weekStartsOn: 1 }), 'yyyy-MM-dd'),
    }
    if (period === 'month') return {
      from: format(startOfMonth(now), 'yyyy-MM-dd'),
      to: format(endOfMonth(now), 'yyyy-MM-dd'),
    }
    return { from: customFrom, to: customTo }
  }

  const { from, to } = getPeriodDates()

  const { data, isLoading, isError } = useQuery({
    queryKey: ['company-stats', selectedId, from, to],
    queryFn: () => statsApi.getCompanyStats(selectedId, from, to),
    enabled: !!selectedId,
  })

  const PERIODS: { key: Period; label: string }[] = [
    { key: 'today', label: 'Сегодня' },
    { key: 'week', label: 'Неделя' },
    { key: 'month', label: 'Месяц' },
    { key: 'custom', label: 'Период' },
  ]

  function renderContent(stats: CompanyStats) {
    return (
      <div className="flex flex-col gap-6">
        {/* Stat tiles */}
        <div className="grid grid-cols-2 gap-3.5 sm:grid-cols-4">
          <StatTile label="Выручка" value={`${stats.totalRevenue.toLocaleString('ru-RU')} ₽`} />
          <StatTile label="Записей" value={String(stats.bookingsCount)} />
          <StatTile label="Выполнено" value={String(stats.completedCount)} />
          <StatTile label="Новых клиентов" value={String(stats.newClientsCount)} />
        </div>

        {/* Daily revenue chart */}
        {stats.dailyRevenue.length > 0 && (
          <div className="bg-white border border-line rounded-[18px] p-[22px]">
            <p className="text-sm font-semibold text-ink mb-4">Выручка по дням</p>
            <BarChart data={stats.dailyRevenue} />
          </div>
        )}

        <div className="grid sm:grid-cols-2 gap-5">
          {/* Master stats */}
          {stats.masterStats.length > 0 && (
            <div className="bg-white border border-line rounded-[18px] p-[22px]">
              <p className="text-sm font-semibold text-ink mb-3.5">Загрузка мастеров</p>
              <div className="flex flex-col">
                {stats.masterStats.map(m => (
                  <div key={m.masterId} className="flex items-center justify-between gap-4 py-2.5 border-b border-cream-deep last:border-0 text-[13.5px]">
                    <span className="text-ink">{m.masterName}</span>
                    <span className="font-semibold text-gold-dark shrink-0">{m.revenue.toLocaleString('ru-RU')} ₽</span>
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* Popular services */}
          {stats.popularServices.length > 0 && (
            <div className="bg-white border border-line rounded-[18px] p-[22px]">
              <p className="text-sm font-semibold text-ink mb-3.5">Популярные услуги</p>
              <div className="flex flex-col">
                {stats.popularServices.slice(0, 5).map(s => (
                  <div key={s.serviceId} className="flex items-center justify-between gap-4 py-2.5 border-b border-cream-deep last:border-0 text-[13.5px]">
                    <span className="text-ink">{s.serviceName}</span>
                    <span className="text-muted shrink-0">{s.count}×</span>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      </div>
    )
  }

  return (
    <div>
      {/* Filters */}
      <div className="flex flex-wrap gap-3 mb-6">
        {companies.length > 1 && (
          <select
            value={selectedId}
            onChange={e => setCompanyId(e.target.value)}
            className="rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold bg-white text-ink"
          >
            {companies.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        )}

        <div className="flex gap-1 bg-cream-deep p-1 rounded-full">
          {PERIODS.map(p => (
            <button
              key={p.key}
              onClick={() => setPeriod(p.key)}
              className={`px-3.5 py-[7px] rounded-full text-sm font-medium transition-all ${period === p.key ? 'bg-white text-ink shadow-sm' : 'text-gold-dark hover:text-ink'}`}
            >
              {p.label}
            </button>
          ))}
        </div>

        {period === 'custom' && (
          <div className="flex items-center gap-2">
            <input
              type="date"
              value={customFrom}
              onChange={e => setCustomFrom(e.target.value)}
              className="rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold bg-white text-ink"
            />
            <span className="text-muted">—</span>
            <input
              type="date"
              value={customTo}
              onChange={e => setCustomTo(e.target.value)}
              className="rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold bg-white text-ink"
            />
          </div>
        )}
      </div>

      {isLoading && (
        <div className="flex flex-col gap-4">
          <div className="grid grid-cols-2 gap-3.5 sm:grid-cols-4">
            {Array.from({ length: 4 }).map((_, i) => <div key={i} className="h-20 bg-cream-deep rounded-2xl animate-pulse" />)}
          </div>
          <div className="h-40 bg-cream-deep rounded-2xl animate-pulse" />
        </div>
      )}

      {isError && (
        <Card className="p-12 text-center text-muted">
          <Icon name="alert-circle" size={32} strokeWidth={1.4} className="mx-auto mb-2" />
          <p>Не удалось загрузить данные</p>
        </Card>
      )}

      {!isLoading && !isError && data && renderContent(data)}

      {!isLoading && !isError && !data && (
        <Card className="p-12 text-center text-muted">
          <Icon name="bar-chart" size={32} strokeWidth={1.4} className="mx-auto mb-2" />
          <p>Нет данных за выбранный период</p>
        </Card>
      )}
    </div>
  )
}
