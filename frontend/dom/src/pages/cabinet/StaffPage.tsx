import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Input } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { PhoneInput } from '@/components/ui/PhoneInput'
import { formatPhone, isRussianPhone } from '@/utils/phone'
import { staysMembersApi, type StaysMemberDto } from '../../api/members'
import { SelectField } from '../../components/cabinet/formParts'
import { EmptyState, ErrorState, InlineError, LoadingList } from '../../components/StatePanels'
import { useStaysCompany } from '../../hooks/useStaysCompany'
import type { StaffPosition } from '../../types'
import { POSITION_LABELS, can } from '../../utils/permissions'
import { getStayErrorMessage } from '../../utils/stayError'
import { NotFoundPage } from '../NotFoundPage'

const POSITION_OPTIONS = (Object.keys(POSITION_LABELS) as StaffPosition[]).map((p) => ({ value: p, label: POSITION_LABELS[p] }))

/**
 * `/cabinet/:companyId/staff` (`ManageCompany`, US-37-08) — staff with a position. A staff member is a `Master` with a position:
 * the manager sees bookings, the board, blocks and can create a booking; the housekeeper sees only the schedule of cleanings and
 * arrivals. Removing a member takes the access away at once.
 */
export function StaffPage() {
  const { company } = useStaysCompany()
  const qc = useQueryClient()
  const key = ['stays-members', company.id]
  const members = useQuery({ queryKey: key, queryFn: () => staysMembersApi.list(company.id) })

  const [phone, setPhone] = useState('')
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [position, setPosition] = useState<StaffPosition>('Housekeeper')
  const [toRemove, setToRemove] = useState<StaysMemberDto | null>(null)

  const add = useMutation({
    mutationFn: () => staysMembersApi.add(company.id, { phone, firstName: firstName.trim(), lastName: lastName.trim(), position }),
    onSuccess: () => {
      setPhone('')
      setFirstName('')
      setLastName('')
      void qc.invalidateQueries({ queryKey: key })
    },
  })
  const move = useMutation({
    mutationFn: (v: { m: StaysMemberDto; position: StaffPosition }) => staysMembersApi.setPosition(company.id, v.m.id, v.position),
    onSettled: () => void qc.invalidateQueries({ queryKey: key }),
  })
  const remove = useMutation({
    mutationFn: (m: StaysMemberDto) => staysMembersApi.remove(company.id, m.id),
    onSuccess: () => {
      setToRemove(null)
      void qc.invalidateQueries({ queryKey: key })
    },
  })

  if (!can(company.myPermissions, 'ManageCompany')) return <NotFoundPage title="Раздел недоступен" hint="Персоналом управляет владелец." />

  const canSubmit = isRussianPhone(phone) && firstName.trim().length > 0 && lastName.trim().length > 0

  return (
    <main className="mx-auto flex max-w-[760px] flex-col gap-6 px-4 pb-6 pt-8 sm:px-8">
      <Card className="p-5 sm:p-6">
        <h2 className="mb-1 font-serif text-[22px] text-ink">Добавить сотрудника</h2>
        <p className="mb-4 text-sm text-ink-soft">
          Если аккаунта с таким номером нет, он будет создан. <span className="font-medium text-ink">Управляющий</span> ведёт брони, шахматку и
          блокировки, правит описание и фото домов. <span className="font-medium text-ink">Горничная</span> видит только график уборок и
          заездов — без телефонов гостей и сумм.
        </p>
        <form
          className="flex flex-col gap-4"
          onSubmit={(e) => {
            e.preventDefault()
            if (canSubmit) add.mutate()
          }}
        >
          <PhoneInput label="Телефон *" value={phone} onChange={setPhone} />
          <div className="grid gap-4 sm:grid-cols-2">
            <Input label="Имя *" value={firstName} onChange={(e) => setFirstName(e.target.value)} maxLength={100} />
            <Input label="Фамилия *" value={lastName} onChange={(e) => setLastName(e.target.value)} maxLength={100} />
          </div>
          <SelectField label="Должность" value={position} onChange={(v) => setPosition(v as StaffPosition)} options={POSITION_OPTIONS} />
          {add.isError && <InlineError>{getStayErrorMessage(add.error, 'Не удалось добавить сотрудника.')}</InlineError>}
          <div>
            <Button type="submit" loading={add.isPending} disabled={!canSubmit} className="min-h-[44px]">
              Добавить
            </Button>
          </div>
        </form>
      </Card>

      <section aria-label="Сотрудники компании">
        <h2 className="mb-3 font-serif text-[22px] text-ink">Команда</h2>
        {members.isLoading ? (
          <LoadingList rows={2} rowClass="h-16" />
        ) : members.isError ? (
          <ErrorState message={getStayErrorMessage(members.error, 'Не удалось загрузить сотрудников.')} onRetry={() => void members.refetch()} />
        ) : !members.data || members.data.length === 0 ? (
          <EmptyState title="Сотрудников пока нет" text="Добавьте управляющего или горничную по номеру телефона." />
        ) : (
          <ul className="flex flex-col gap-2">
            {members.data.map((m) => {
              const isOwner = m.role === 'CompanyOwner'
              return (
                <li key={m.id} className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-line bg-white px-5 py-3.5">
                  <div className="min-w-0">
                    <p className="truncate font-medium text-ink">
                      {m.firstName} {m.lastName}
                    </p>
                    <p className="text-xs text-muted">
                      {formatPhone(m.phone)} · {isOwner ? 'Владелец' : m.position ? POSITION_LABELS[m.position] : 'Сотрудник'}
                    </p>
                  </div>
                  {!isOwner && (
                    <div className="flex items-center gap-2">
                      <label className="sr-only" htmlFor={`pos-${m.id}`}>
                        Должность: {m.firstName} {m.lastName}
                      </label>
                      <select
                        id={`pos-${m.id}`}
                        value={m.position ?? ''}
                        disabled={move.isPending}
                        onChange={(e) => move.mutate({ m, position: e.target.value as StaffPosition })}
                        className="min-h-[44px] rounded-xl border border-line bg-white px-3 text-sm text-ink"
                      >
                        {!m.position && <option value="">—</option>}
                        {POSITION_OPTIONS.map((o) => (
                          <option key={o.value} value={o.value}>
                            {o.label}
                          </option>
                        ))}
                      </select>
                      <Button variant="danger" size="sm" className="min-h-[44px]" onClick={() => setToRemove(m)}>
                        Удалить
                      </Button>
                    </div>
                  )}
                </li>
              )
            })}
          </ul>
        )}
        {move.isError && (
          <div className="mt-3">
            <InlineError>{getStayErrorMessage(move.error, 'Не удалось изменить должность.')}</InlineError>
          </div>
        )}
      </section>

      {toRemove && (
        <Modal title="Удалить сотрудника?" onClose={() => setToRemove(null)} dismissible={!remove.isPending}>
          <p className="text-sm text-ink-soft">
            {toRemove.firstName} {toRemove.lastName} потеряет доступ к компании сразу.
          </p>
          {remove.isError && (
            <div className="mt-3">
              <InlineError>{getStayErrorMessage(remove.error, 'Не удалось удалить сотрудника.')}</InlineError>
            </div>
          )}
          <div className="mt-5 flex gap-3">
            <Button variant="secondary" className="min-h-[44px] flex-1" onClick={() => setToRemove(null)} disabled={remove.isPending}>
              Отмена
            </Button>
            <Button variant="danger" className="min-h-[44px] flex-1" loading={remove.isPending} onClick={() => remove.mutate(toRemove)}>
              Удалить
            </Button>
          </div>
        </Modal>
      )}
    </main>
  )
}
