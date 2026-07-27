import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import { mastersApi, type MasterClient } from '../api/masters'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'

const STATUS_LABELS: Record<string, string> = {
  Pending: 'Ожидает',
  Confirmed: 'Подтверждена',
  Completed: 'Выполнена',
  Cancelled: 'Отменена',
  NoShow: 'Не пришёл',
}

interface ClientCardProps {
  client: MasterClient
  companyId: string
}

function ClientCard({ client, companyId }: ClientCardProps) {
  const [expanded, setExpanded] = useState(false)
  const [newNote, setNewNote] = useState('')
  const qc = useQueryClient()

  const addNoteMut = useMutation({
    mutationFn: () =>
      mastersApi.addNote({
        companyId,
        clientId: client.clientId ?? undefined,
        guestPhone: client.guestPhone ?? undefined,
        note: newNote,
      }),
    onSuccess: () => {
      setNewNote('')
      qc.invalidateQueries({ queryKey: ['master-clients', companyId] })
    },
  })

  const lastVisit = (() => {
    try {
      return format(parseISO(client.lastVisitDate), 'd MMM yyyy', { locale: ru })
    } catch {
      return client.lastVisitDate
    }
  })()

  return (
    <Card className="p-4">
      <button
        onClick={() => setExpanded(v => !v)}
        className="w-full text-left flex items-center justify-between gap-4"
      >
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded-full bg-gradient-to-br from-primary-400 to-accent-500 flex items-center justify-center text-white font-bold text-sm shrink-0">
            {client.name[0]?.toUpperCase() ?? '?'}
          </div>
          <div>
            <p className="font-semibold text-gray-900">{client.name}</p>
            <p className="text-xs text-gray-400">Последний визит: {lastVisit} · {client.totalVisits} {client.totalVisits === 1 ? 'визит' : client.totalVisits < 5 ? 'визита' : 'визитов'}</p>
          </div>
        </div>
        <div className="text-right shrink-0">
          {client.phone ? (
            <a href={`tel:${client.phone}`} onClick={e => e.stopPropagation()} className="text-sm text-primary-600 hover:underline">{client.phone}</a>
          ) : (
            <span className="text-xs text-gray-400">📵 Доступен 24 ч после визита</span>
          )}
          {client.email && <p className="text-xs text-gray-400">{client.email}</p>}
        </div>
        <span className="text-gray-400 text-sm shrink-0">{expanded ? '▲' : '▼'}</span>
      </button>

      {expanded && (
        <div className="mt-4 border-t border-gray-100 pt-4 flex flex-col gap-4">
          {/* Visit history */}
          <div>
            <p className="text-sm font-semibold text-gray-700 mb-2">История визитов</p>
            {(client.bookingSummaries ?? []).length === 0 ? (
              <p className="text-sm text-gray-400">Нет записей</p>
            ) : (
              <div className="flex flex-col gap-1.5">
                {(client.bookingSummaries ?? []).map((b, i) => (
                  <div key={i} className="flex items-center gap-3 text-sm">
                    <span className="text-gray-500 shrink-0">{b.date}</span>
                    <span className="text-gray-800 flex-1">{b.serviceName}</span>
                    <span className={`text-xs px-2 py-0.5 rounded-full shrink-0 ${
                      b.status === 'Completed' ? 'bg-green-100 text-green-700'
                      : b.status === 'Cancelled' ? 'bg-red-100 text-red-600'
                      : 'bg-gray-100 text-gray-600'
                    }`}>{STATUS_LABELS[b.status] ?? b.status}</span>
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* Notes */}
          <div>
            <p className="text-sm font-semibold text-gray-700 mb-2">Заметки</p>
            {client.notes.length === 0 ? (
              <p className="text-sm text-gray-400 mb-2">Нет заметок</p>
            ) : (
              <ul className="flex flex-col gap-1 mb-2">
                {client.notes.map((n, i) => (
                  <li key={i} className="text-sm text-gray-700 bg-gray-50 rounded-xl px-3 py-2">
                    {n}
                  </li>
                ))}
              </ul>
            )}
            <div className="flex gap-2 mt-2">
              <input
                type="text"
                value={newNote}
                onChange={e => setNewNote(e.target.value)}
                placeholder="Добавить заметку…"
                className="flex-1 rounded-xl border border-gray-200 px-3 py-2 text-sm outline-none focus:border-primary-400"
                onKeyDown={e => { if (e.key === 'Enter' && newNote.trim()) addNoteMut.mutate() }}
              />
              <Button
                size="sm"
                onClick={() => addNoteMut.mutate()}
                disabled={!newNote.trim()}
                loading={addNoteMut.isPending}
              >
                Добавить
              </Button>
            </div>
          </div>
        </div>
      )}
    </Card>
  )
}

interface Props {
  companyId: string
}

export function MasterClientsPage({ companyId }: Props) {
  const [search, setSearch] = useState('')

  const { data: clients, isLoading, isError } = useQuery({
    queryKey: ['master-clients', companyId],
    queryFn: () => mastersApi.getClients(companyId),
    enabled: !!companyId,
  })

  const filtered = (clients ?? []).filter(c =>
    c.name.toLowerCase().includes(search.toLowerCase())
  )

  if (isLoading) {
    return (
      <div className="flex flex-col gap-3">
        {Array.from({ length: 3 }).map((_, i) => (
          <div key={i} className="h-20 bg-gray-100 rounded-2xl animate-pulse" />
        ))}
      </div>
    )
  }

  if (isError) {
    return (
      <Card className="p-12 text-center text-gray-400">
        <p className="text-3xl mb-2">⚠️</p>
        <p>Не удалось загрузить клиентов</p>
      </Card>
    )
  }

  return (
    <div>
      <div className="flex items-center justify-between mb-5">
        <h2 className="text-lg font-semibold text-gray-900">Мои клиенты</h2>
        <span className="text-sm text-gray-400">{filtered.length} клиентов</span>
      </div>

      <div className="mb-4">
        <input
          type="text"
          value={search}
          onChange={e => setSearch(e.target.value)}
          placeholder="🔍 Поиск по имени…"
          className="w-full rounded-xl border border-gray-200 px-3 py-2.5 text-sm outline-none focus:border-primary-400"
        />
      </div>

      {filtered.length === 0 ? (
        <Card className="p-12 text-center text-gray-400">
          <p className="text-4xl mb-3">👥</p>
          <p className="text-lg font-medium text-gray-600">
            {search ? 'Клиентов не найдено' : 'У вас пока нет клиентов'}
          </p>
          {!search && <p className="text-sm mt-1">Здесь появятся клиенты после первых записей</p>}
        </Card>
      ) : (
        <div className="flex flex-col gap-3">
          {filtered.map(c => (
            <ClientCard
              key={c.clientId ?? c.guestPhone ?? c.name}
              client={c}
              companyId={companyId}
            />
          ))}
        </div>
      )}
    </div>
  )
}
