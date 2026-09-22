import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import { subjectRequestsApi } from '../../api/subjectRequests'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { Modal } from '../../components/ui/Modal'
import { Icon } from '../../components/ui/Icon'
import { Pagination } from '../../components/ui/Pagination'
import { getSubjectRequestAdminErrorMessage } from '../../utils/subjectRequestError'
import type { DueState, SubjectRequestDto, SubjectRequestKind, SubjectRequestStatus } from '../../types'

function fmt(iso: string) {
  return format(parseISO(iso), 'd MMM yyyy, HH:mm', { locale: ru })
}

const KIND_LABEL: Record<SubjectRequestKind, string> = {
  Access: 'Доступ',
  Rectification: 'Уточнение',
  Erasure: 'Удаление',
  ConsentWithdrawal: 'Отзыв согласия',
  Complaint: 'Жалоба',
}

const STATUS_LABEL: Record<SubjectRequestStatus, string> = {
  Received: 'Принято',
  InProgress: 'В работе',
  Answered: 'Отвечено',
  Rejected: 'Отклонено',
}

const DUE_CLASS: Record<DueState, string> = {
  OnTime: 'bg-cream-deep text-ink-soft',
  DueSoon: 'bg-warning-bg text-warning',
  Overdue: 'bg-danger-bg text-danger',
}

const DUE_LABEL: Record<DueState, string> = {
  OnTime: 'в срок',
  DueSoon: 'срок скоро истекает',
  Overdue: 'просрочено',
}

function AnswerModal({ request, onClose }: { request: SubjectRequestDto; onClose: () => void }) {
  const qc = useQueryClient()
  const [status, setStatus] = useState<SubjectRequestStatus>('Answered')
  const [resolution, setResolution] = useState('')

  const mut = useMutation({
    mutationFn: () => subjectRequestsApi.adminSetStatus(request.id, status, resolution),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['admin-subject-requests'] })
      onClose()
    },
  })

  return (
    <Modal title={`Обращение ${request.reference}`} onClose={onClose}>
      <div className="flex flex-col gap-4">
        <div className="text-sm text-ink-soft flex flex-col gap-1">
          <p>
            <strong>{KIND_LABEL[request.kind]}</strong> · {request.phoneMasked} · {request.contactValue}
          </p>
          <p className="whitespace-pre-wrap">{request.message}</p>
        </div>

        <div className="flex flex-col gap-1.5">
          <label className="text-[13px] font-medium text-[#4A4038]">Статус</label>
          <select
            value={status}
            onChange={(e) => setStatus(e.target.value as SubjectRequestStatus)}
            className="rounded-xl border border-line px-4 py-3 text-sm outline-none focus:border-gold bg-white text-ink"
          >
            <option value="InProgress">В работе</option>
            <option value="Answered">Отвечено</option>
            <option value="Rejected">Отклонено</option>
          </select>
        </div>

        <div className="flex flex-col gap-1.5">
          <label className="text-[13px] font-medium text-[#4A4038]">Результат</label>
          <textarea
            rows={3}
            value={resolution}
            onChange={(e) => setResolution(e.target.value)}
            placeholder="Данные удалены, ответ отправлен на e-mail"
            className="rounded-xl border border-line px-4 py-3 text-sm outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep resize-none bg-white text-ink"
          />
        </div>

        {mut.isError && <p className="text-sm text-danger">{getSubjectRequestAdminErrorMessage(mut.error)}</p>}

        <div className="flex gap-3 pt-1">
          <Button variant="secondary" className="flex-1" onClick={onClose}>
            Отмена
          </Button>
          <Button
            className="flex-1"
            disabled={(status === 'Answered' || status === 'Rejected') && !resolution.trim()}
            loading={mut.isPending}
            onClick={() => mut.mutate()}
          >
            Сохранить
          </Button>
        </div>
      </div>
    </Modal>
  )
}

/** API_CONTRACT_CYCLE5.md §48.2–48.3, ARCHITECTURE_CYCLE5.md T5-F8 — sorted by `dueAt` ascending by
 *  the server (§48.2), so the most urgent request is already first without any client-side sort. */
export function SubjectRequestsTab() {
  const [page, setPage] = useState(1)
  const [status, setStatus] = useState<SubjectRequestStatus | ''>('')
  const [dueState, setDueState] = useState<DueState | ''>('')
  const [answering, setAnswering] = useState<SubjectRequestDto | null>(null)

  const { data, isLoading, isError } = useQuery({
    queryKey: ['admin-subject-requests', page, status, dueState],
    queryFn: () =>
      subjectRequestsApi.adminList({
        page,
        pageSize: 20,
        status: status || undefined,
        dueState: dueState || undefined,
      }),
  })

  return (
    <div>
      <div className="flex flex-wrap gap-3 mb-4">
        <select
          value={status}
          onChange={(e) => {
            setStatus(e.target.value as SubjectRequestStatus | '')
            setPage(1)
          }}
          className="rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold bg-white text-ink"
        >
          <option value="">Все статусы</option>
          {Object.entries(STATUS_LABEL).map(([v, l]) => (
            <option key={v} value={v}>
              {l}
            </option>
          ))}
        </select>
        <select
          value={dueState}
          onChange={(e) => {
            setDueState(e.target.value as DueState | '')
            setPage(1)
          }}
          className="rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold bg-white text-ink"
        >
          <option value="">Любой срок</option>
          {Object.entries(DUE_LABEL).map(([v, l]) => (
            <option key={v} value={v}>
              {l}
            </option>
          ))}
        </select>
      </div>

      {isLoading ? (
        <div className="grid gap-2">
          {Array.from({ length: 4 }).map((_, i) => (
            <div key={i} className="h-16 bg-cream-deep rounded-2xl animate-pulse" />
          ))}
        </div>
      ) : isError || !data ? (
        <Card className="p-10 text-center text-muted">
          <Icon name="alert-circle" size={28} strokeWidth={1.6} className="mx-auto mb-2" />
          <p>Не удалось загрузить обращения.</p>
        </Card>
      ) : data.items.length === 0 ? (
        <Card className="p-10 text-center text-muted">
          <p>Обращений пока нет</p>
        </Card>
      ) : (
        <div className="grid gap-2">
          {data.items.map((r) => (
            <Card key={r.id} className="p-4 flex items-center justify-between gap-3 flex-wrap">
              <div className="min-w-0">
                <div className="flex items-center gap-2 flex-wrap">
                  <span className="font-medium text-ink text-sm">{r.reference}</span>
                  <span className="text-xs text-muted">{KIND_LABEL[r.kind]}</span>
                  <span className="text-xs px-2 py-0.5 rounded-full bg-cream-deep text-ink-soft">{STATUS_LABEL[r.status]}</span>
                  <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${DUE_CLASS[r.dueState]}`}>{DUE_LABEL[r.dueState]}</span>
                </div>
                <p className="text-xs text-muted mt-0.5">
                  {r.phoneMasked} · поступило {fmt(r.receivedAt)} · срок до {fmt(r.dueAt)}
                </p>
              </div>
              <Button size="sm" variant="secondary" className="shrink-0" onClick={() => setAnswering(r)}>
                Обработать
              </Button>
            </Card>
          ))}
        </div>
      )}

      {data && <Pagination page={data.page} pageSize={data.pageSize} total={data.total} hasNext={data.hasNext} onPageChange={setPage} />}

      {answering && <AnswerModal request={answering} onClose={() => setAnswering(null)} />}
    </div>
  )
}
