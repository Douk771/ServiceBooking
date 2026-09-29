import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { companiesApi, type MemberDto } from '@/api/companies'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Input } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { PhoneInput } from '@/components/ui/PhoneInput'
import { formatPhone, isRussianPhone } from '@/utils/phone'
import { getAddMemberErrorMessage } from '@/utils/memberError'
import { useShopContext } from '../../hooks/useShop'
import { EmptyState, ErrorState, InlineError, LoadingList } from '../../components/StatePanels'
import { getGoodsErrorMessage } from '../../utils/orderError'

/**
 * US-23-11 — owner-only staff list over the existing `/api/companies/{id}/members`. A shop staff member is a
 * `Master` (shown as «Сотрудник»); the API refuses any other role for a shop (§408.7), so none is offered.
 */
export function StaffPage() {
  const { shop } = useShopContext()
  const qc = useQueryClient()
  const key = ['shop-members', shop.id]
  const members = useQuery({ queryKey: key, queryFn: () => companiesApi.getMembers(shop.id) })

  const [phone, setPhone] = useState('')
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [toRemove, setToRemove] = useState<MemberDto | null>(null)

  const add = useMutation({
    mutationFn: () => companiesApi.addMember(shop.id, phone, firstName.trim(), lastName.trim(), 'Master'),
    onSuccess: () => {
      setPhone('')
      setFirstName('')
      setLastName('')
      void qc.invalidateQueries({ queryKey: key })
    },
  })
  const remove = useMutation({
    mutationFn: (m: MemberDto) => companiesApi.removeMember(shop.id, m.id),
    onSuccess: () => {
      setToRemove(null)
      void qc.invalidateQueries({ queryKey: key })
    },
  })

  const canSubmit = isRussianPhone(phone) && firstName.trim().length > 0 && lastName.trim().length > 0

  return (
    <main className="max-w-[760px] mx-auto px-4 sm:px-8 pt-8 flex flex-col gap-5">
      <Card className="p-6">
        <h2 className="text-[15px] font-semibold text-ink mb-1">Добавить сотрудника</h2>
        <p className="text-sm text-ink-soft mb-4">
          Сотрудник видит экран заказов, меняет статусы и состав заказов, отмечает «закончилось» и правит остатки. Товары, цены,
          настройки и сотрудников менять не может. Если аккаунта с таким номером нет, он будет создан.
        </p>
        <form
          className="flex flex-col gap-4"
          onSubmit={(e) => {
            e.preventDefault()
            if (canSubmit) add.mutate()
          }}
        >
          <PhoneInput label="Телефон *" value={phone} onChange={setPhone} />
          <div className="grid sm:grid-cols-2 gap-4">
            <Input label="Имя *" value={firstName} onChange={(e) => setFirstName(e.target.value)} maxLength={100} />
            <Input label="Фамилия *" value={lastName} onChange={(e) => setLastName(e.target.value)} maxLength={100} />
          </div>
          {add.isError && <InlineError>{getAddMemberErrorMessage(add.error)}</InlineError>}
          <div>
            <Button type="submit" loading={add.isPending} disabled={!canSubmit}>
              Добавить
            </Button>
          </div>
        </form>
      </Card>

      <section aria-label="Сотрудники магазина">
        <h2 className="text-[15px] font-semibold text-ink mb-3">Команда</h2>
        {members.isLoading ? (
          <LoadingList rows={2} rowClass="h-16" />
        ) : members.isError ? (
          <ErrorState message={getGoodsErrorMessage(members.error, 'Не удалось загрузить сотрудников.')} onRetry={() => void members.refetch()} />
        ) : !members.data || members.data.length === 0 ? (
          <EmptyState title="Сотрудников пока нет" text="Добавьте сотрудника по номеру телефона." />
        ) : (
          <ul className="flex flex-col gap-2">
            {members.data.map((m) => {
              const isOwner = m.role === 'CompanyOwner'
              return (
                <li key={m.id} className="flex items-center justify-between gap-3 rounded-2xl border border-line bg-white px-5 py-3.5">
                  <div className="min-w-0">
                    <p className="font-medium text-ink truncate">
                      {m.firstName} {m.lastName}
                    </p>
                    <p className="text-xs text-muted">
                      {formatPhone(m.phone)} · {isOwner ? 'Владелец' : 'Сотрудник'}
                    </p>
                  </div>
                  {!isOwner && (
                    <Button variant="danger" size="sm" onClick={() => setToRemove(m)}>
                      Удалить
                    </Button>
                  )}
                </li>
              )
            })}
          </ul>
        )}
      </section>

      {toRemove && (
        <Modal title="Удалить сотрудника?" onClose={() => setToRemove(null)} dismissible={!remove.isPending}>
          <p className="text-sm text-ink-soft">
            {toRemove.firstName} {toRemove.lastName} потеряет доступ к магазину сразу.
          </p>
          {remove.isError && <div className="mt-3"><InlineError>{getGoodsErrorMessage(remove.error, 'Не удалось удалить сотрудника.')}</InlineError></div>}
          <div className="flex gap-3 mt-5">
            <Button variant="secondary" className="flex-1" onClick={() => setToRemove(null)} disabled={remove.isPending}>
              Отмена
            </Button>
            <Button variant="danger" className="flex-1" loading={remove.isPending} onClick={() => remove.mutate(toRemove)}>
              Удалить
            </Button>
          </div>
        </Modal>
      )}
    </main>
  )
}
