import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { legalApi } from '../../api/legal'
import { useAuthStore } from '../../store/authStore'
import { useOwnerGateStore } from '../../store/ownerGateStore'
import { Modal } from '../ui/Modal'
import { Button } from '../ui/Button'
import { getLegalErrorMessage } from '../../utils/legalError'

/**
 * Owner-scope 451 (API_CONTRACT_CYCLE5.md §38.2, §42.2, T5-F4). Mounted once at the app root
 * (`App.tsx`) so it can open from anywhere an owner action fails this way — company settings, staff,
 * templates, channel screens. Unlike `ConsentGate`, this never covers the screen: reading keeps
 * working while it's up, and the owner can simply dismiss it and come back later.
 *
 * After accepting, the action that tripped the gate is NOT automatically retried — the owner presses
 * the same button again, same as after `RiskAcceptanceModal`. Automatic retry would need threading a
 * retry callback through every one of the nine gated endpoints for a one-click saving that the
 * contract doesn't ask for.
 */
export function OwnerTermsGateModal() {
  const pending = useOwnerGateStore((s) => s.pending)
  const setPending = useOwnerGateStore((s) => s.setPending)
  const { user, setAuth } = useAuthStore()
  const qc = useQueryClient()
  const [checked, setChecked] = useState(false)

  const mut = useMutation({
    mutationFn: () => {
      if (!pending) throw new Error('no pending owner gate')
      return legalApi.accept([{ type: pending.documentType, version: pending.version }])
    },
    onSuccess: (res) => {
      if (user) setAuth(user, res.token)
      qc.invalidateQueries({ queryKey: ['legal-consent-status'] })
      setChecked(false)
      setPending(null)
    },
  })

  if (!pending) return null

  const close = () => {
    setChecked(false)
    mut.reset()
    setPending(null)
  }

  return (
    <Modal title="Обновилось соглашение с компанией" onClose={close}>
      <div className="flex flex-col gap-4">
        <p className="text-sm text-ink-soft leading-[1.6]">
          Чтобы изменять данные компании или подключать платные функции, нужно принять новую редакцию соглашения с
          компанией. Читать расписание, записи и клиентскую базу это не ограничивает.
        </p>
        <Link
          to="/terms-owner"
          target="_blank"
          className="text-sm text-gold hover:text-gold-dark w-fit"
        >
          Соглашение с компанией и поручение на обработку персональных данных →
        </Link>

        <label className="flex items-start gap-2.5 cursor-pointer">
          <input
            type="checkbox"
            className="w-4 h-4 mt-0.5 rounded accent-gold"
            checked={checked}
            onChange={(e) => setChecked(e.target.checked)}
          />
          <span className="text-sm text-ink-soft">Я прочитал(а) и принимаю новую редакцию соглашения</span>
        </label>

        {mut.isError && <p className="text-sm text-danger">{getLegalErrorMessage(mut.error)}</p>}

        {mut.isSuccess ? (
          <p className="text-sm text-success">Принято. Повторите действие, которое было заблокировано.</p>
        ) : (
          <div className="flex gap-3 pt-1">
            <Button variant="secondary" className="flex-1" onClick={close}>
              Позже
            </Button>
            <Button className="flex-1" disabled={!checked} loading={mut.isPending} onClick={() => mut.mutate()}>
              Принимаю
            </Button>
          </div>
        )}
      </div>
    </Modal>
  )
}
