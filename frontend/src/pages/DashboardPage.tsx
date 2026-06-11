import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { format } from 'date-fns'
import { bookingsApi } from '../api/bookings'
import { companiesApi } from '../api/companies'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { StatusBadge } from '../components/ui/Badge'
import { ScheduleTab } from './owner/ScheduleTab'

// ── Bookings tab ──────────────────────────────────────────────────────────────

function BookingsTab() {
  const [date, setDate] = useState(format(new Date(), 'yyyy-MM-dd'))
  const qc = useQueryClient()

  const { data: bookings, isLoading } = useQuery({
    queryKey: ['master-bookings', date],
    queryFn: () => bookingsApi.getMasterBookings(date),
  })

  const cancel = useMutation({
    mutationFn: (id: string) => bookingsApi.cancel(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['master-bookings'] }),
  })

  return (
    <div>
      <Card className="p-6 mb-6">
        <label className="block text-sm font-medium text-gray-700 mb-2">Выберите дату</label>
        <input
          type="date"
          value={date}
          onChange={(e) => setDate(e.target.value)}
          className="rounded-xl border border-gray-200 px-3 py-2 text-sm outline-none focus:border-primary-400 focus:ring-2 focus:ring-primary-100"
        />
      </Card>

      <h2 className="text-lg font-semibold text-gray-700 mb-4">
        Записи на {date} {bookings && `(${bookings.length})`}
      </h2>

      {isLoading ? (
        <div className="grid gap-4">
          {Array.from({ length: 4 }).map((_, i) => <div key={i} className="h-20 bg-gray-100 rounded-2xl animate-pulse" />)}
        </div>
      ) : bookings && bookings.length > 0 ? (
        <div className="grid gap-3">
          {bookings.map((b) => (
            <Card key={b.id} className="p-4 flex items-center justify-between gap-4">
              <div className="flex items-center gap-4">
                <div className="text-center bg-orange-50 rounded-xl px-3 py-2 min-w-[60px]">
                  <div className="text-lg font-bold text-primary-600">{b.startTime.slice(0, 5)}</div>
                  <div className="text-xs text-gray-400">{b.endTime.slice(0, 5)}</div>
                </div>
                <div>
                  <div className="flex items-center gap-2">
                    <span className="font-medium text-gray-900">{b.clientName}</span>
                    <StatusBadge status={b.status} />
                  </div>
                  <p className="text-sm text-gray-500">{b.serviceName}</p>
                  {b.clientPhone && <p className="text-xs text-gray-400 mt-0.5">📞 {b.clientPhone}</p>}
                </div>
              </div>
              {(b.status === 'Pending' || b.status === 'Confirmed') && (
                <Button
                  variant="danger"
                  size="sm"
                  loading={cancel.isPending}
                  onClick={() => cancel.mutate(b.id)}
                >
                  Отменить
                </Button>
              )}
            </Card>
          ))}
        </div>
      ) : (
        <div className="text-center py-12 text-gray-400">
          <p className="text-3xl mb-2">📅</p>
          <p>Записей на этот день нет</p>
        </div>
      )}
    </div>
  )
}

// ── Schedule tab ──────────────────────────────────────────────────────────────

function MasterScheduleTab() {
  const { data: companies, isLoading } = useQuery({
    queryKey: ['member-companies'],
    queryFn: () => companiesApi.getMemberOf(),
  })

  const [selectedCompanyId, setSelectedCompanyId] = useState<string>('')
  const companyId = selectedCompanyId || companies?.[0]?.id || ''

  if (isLoading) {
    return <div className="h-40 bg-gray-100 rounded-2xl animate-pulse" />
  }

  if (!companies || companies.length === 0) {
    return (
      <Card className="p-12 text-center text-gray-400">
        <p className="text-3xl mb-2">🏢</p>
        <p>Вы не привязаны ни к одной компании</p>
      </Card>
    )
  }

  return (
    <div>
      {companies.length > 1 && (
        <div className="flex flex-wrap gap-2 mb-4">
          {companies.map(c => (
            <button
              key={c.id}
              onClick={() => setSelectedCompanyId(c.id)}
              className={`px-4 py-2 rounded-xl border text-sm font-medium transition-all ${
                c.id === companyId
                  ? 'bg-primary-50 border-primary-300 text-primary-700'
                  : 'bg-white border-gray-200 text-gray-500 hover:border-gray-300'
              }`}
            >
              {c.name}
            </button>
          ))}
        </div>
      )}

      {companyId && <ScheduleTab companyId={companyId} />}
    </div>
  )
}

// ── Main page ─────────────────────────────────────────────────────────────────

type Tab = 'bookings' | 'schedule'

export function DashboardPage() {
  const [tab, setTab] = useState<Tab>('bookings')

  return (
    <div className="max-w-4xl mx-auto px-4 py-8">
      <h1 className="text-2xl font-bold text-gray-900 mb-6">Кабинет мастера</h1>

      {/* Tabs */}
      <div className="flex gap-1 bg-gray-100 rounded-2xl p-1 mb-6 w-fit">
        {([['bookings', 'Записи'], ['schedule', 'Расписание']] as [Tab, string][]).map(([key, label]) => (
          <button
            key={key}
            onClick={() => setTab(key)}
            className={`px-5 py-2 rounded-xl text-sm font-medium transition-all ${
              tab === key
                ? 'bg-white text-gray-900 shadow-sm'
                : 'text-gray-500 hover:text-gray-700'
            }`}
          >
            {label}
          </button>
        ))}
      </div>

      {tab === 'bookings' && <BookingsTab />}
      {tab === 'schedule' && <MasterScheduleTab />}
    </div>
  )
}
