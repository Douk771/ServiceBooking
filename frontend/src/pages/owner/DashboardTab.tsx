import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { format, startOfDay, endOfDay, startOfWeek, endOfWeek, startOfMonth, endOfMonth } from 'date-fns'
import { statsApi, type CompanyStats } from '../../api/stats'
import { Card } from '../../components/ui/Card'

type Period = 'today' | 'week' | 'month' | 'custom'

interface Props {
  companies: { id: string; name: string }[]
}

function StatTile({ icon, label, value }: { icon: string; label: string; value: string }) {
  return (
    <Card className="p-5 flex items-center gap-4">
      <span className="text-3xl shrink-0">{icon}</span>
      <div>
        <p className="text-sm text-gray-500">{label}</p>
        <p className="text-xl font-bold text-gray-900">{value}</p>
      </div>
    </Card>
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
              className="w-full bg-primary-400 rounded-t-md transition-all"
              style={{ height: `${Math.max(pct, 2)}%` }}
              title={`${d.revenue.toLocaleString('ru-RU')} ₽`}
            />
            <span className="text-[10px] text-gray-400 rotate-45 origin-left">{label}</span>
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
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          <StatTile icon="💰" label="Выручка" value={`${stats.totalRevenue.toLocaleString('ru-RU')} ₽`} />
          <StatTile icon="📅" label="Записей" value={String(stats.bookingsCount)} />
          <StatTile icon="✅" label="Выполнено" value={String(stats.completedCount)} />
          <StatTile icon="👥" label="Новых клиентов" value={String(stats.newClientsCount)} />
        </div>

        {/* Daily revenue chart */}
        {stats.dailyRevenue.length > 0 && (
          <Card className="p-5">
            <p className="text-sm font-semibold text-gray-700 mb-3">Выручка по дням</p>
            <BarChart data={stats.dailyRevenue} />
          </Card>
        )}

        {/* Master stats */}
        {stats.masterStats.length > 0 && (
          <Card className="p-5">
            <p className="text-sm font-semibold text-gray-700 mb-3">Загрузка мастеров</p>
            <div className="flex flex-col gap-2">
              {stats.masterStats.map(m => (
                <div key={m.masterId} className="flex items-center justify-between gap-4 py-2 border-b border-gray-50 last:border-0">
                  <span className="text-sm text-gray-800">{m.masterName}</span>
                  <div className="flex gap-4 text-sm text-right shrink-0">
                    <span className="text-gray-500">{m.bookingsCount} зап.</span>
                    <span className="font-semibold text-primary-600">{m.revenue.toLocaleString('ru-RU')} ₽</span>
                  </div>
                </div>
              ))}
            </div>
          </Card>
        )}

        {/* Popular services */}
        {stats.popularServices.length > 0 && (
          <Card className="p-5">
            <p className="text-sm font-semibold text-gray-700 mb-3">Популярные услуги</p>
            <div className="flex flex-col gap-2">
              {stats.popularServices.slice(0, 5).map((s, i) => (
                <div key={s.serviceId} className="flex items-center gap-3">
                  <span className="text-xs font-bold text-gray-400 w-4 shrink-0">{i + 1}</span>
                  <span className="text-sm text-gray-800 flex-1">{s.serviceName}</span>
                  <span className="text-sm font-semibold text-gray-600 shrink-0">{s.count}×</span>
                </div>
              ))}
            </div>
          </Card>
        )}
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
            className="rounded-xl border border-gray-200 px-3 py-2 text-sm outline-none focus:border-primary-400"
          >
            {companies.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        )}

        <div className="flex gap-1 bg-gray-100 p-1 rounded-xl">
          {PERIODS.map(p => (
            <button
              key={p.key}
              onClick={() => setPeriod(p.key)}
              className={`px-3 py-1.5 rounded-lg text-sm font-medium transition-all ${period === p.key ? 'bg-white text-gray-900 shadow-sm' : 'text-gray-500 hover:text-gray-700'}`}
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
              className="rounded-xl border border-gray-200 px-3 py-2 text-sm outline-none focus:border-primary-400"
            />
            <span className="text-gray-400">—</span>
            <input
              type="date"
              value={customTo}
              onChange={e => setCustomTo(e.target.value)}
              className="rounded-xl border border-gray-200 px-3 py-2 text-sm outline-none focus:border-primary-400"
            />
          </div>
        )}
      </div>

      {isLoading && (
        <div className="flex flex-col gap-4">
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            {Array.from({ length: 4 }).map((_, i) => <div key={i} className="h-20 bg-gray-100 rounded-2xl animate-pulse" />)}
          </div>
          <div className="h-40 bg-gray-100 rounded-2xl animate-pulse" />
        </div>
      )}

      {isError && (
        <Card className="p-12 text-center text-gray-400">
          <p className="text-3xl mb-2">⚠️</p>
          <p>Не удалось загрузить данные</p>
        </Card>
      )}

      {!isLoading && !isError && data && renderContent(data)}

      {!isLoading && !isError && !data && (
        <Card className="p-12 text-center text-gray-400">
          <p className="text-3xl mb-2">📊</p>
          <p>Нет данных за выбранный период</p>
        </Card>
      )}
    </div>
  )
}
