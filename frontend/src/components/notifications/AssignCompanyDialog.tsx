import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { notificationChannelsApi } from '../../api/notificationChannels'
import { Modal } from '../ui/Modal'
import { Button } from '../ui/Button'
import { getNotificationErrorMessage } from '../../utils/notificationError'
import type { Company } from '../../types'

interface Props {
  channelId: string
  existingCompanyCount: number
  candidateCompanies: Company[]
  onClose: () => void
}

/**
 * US-61 п. 3 — the three-point warning about sharing one number between companies is a mandatory
 * interface element shown *at the moment of assignment*, not tucked into a help page. It appears as
 * soon as the owner is about to assign a SECOND (or later) company onto a channel that already has
 * one — before the confirming request, not after a 409 from the server.
 */
export function AssignCompanyDialog({ channelId, existingCompanyCount, candidateCompanies, onClose }: Props) {
  const qc = useQueryClient()
  const [companyId, setCompanyId] = useState(candidateCompanies[0]?.id ?? '')
  const [warningAcknowledged, setWarningAcknowledged] = useState(false)
  const needsWarning = existingCompanyCount >= 1

  const mut = useMutation({
    mutationFn: () => notificationChannelsApi.assignCompany(channelId, companyId, warningAcknowledged),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['notification-channels'] })
      onClose()
    },
  })

  return (
    <Modal title="Назначить компанию на канал" onClose={onClose}>
      <div className="flex flex-col gap-4">
        {candidateCompanies.length === 0 ? (
          <p className="text-sm text-muted">Все ваши компании уже назначены на какой-либо канал.</p>
        ) : (
          <div className="flex flex-col gap-1.5">
            <label className="text-[13px] font-medium text-[#4A4038]">Компания</label>
            <select
              value={companyId}
              onChange={(e) => setCompanyId(e.target.value)}
              className="rounded-xl border border-line px-4 py-3 text-sm outline-none focus:border-gold bg-white text-ink"
            >
              {candidateCompanies.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
          </div>
        )}

        {needsWarning && candidateCompanies.length > 0 && (
          <div className="rounded-xl bg-warning-bg border border-[#EAD9AC] px-4 py-3">
            <p className="text-sm font-semibold text-warning mb-1.5">На этом номере уже есть компания</p>
            <ul className="list-disc pl-5 text-sm text-warning/90 flex flex-col gap-1">
              <li>если номер заблокируют, сообщения перестанут уходить у всех компаний на нём сразу;</li>
              <li>один номер от имени разных брендов выглядит для WhatsApp как массовая рассылка и ускоряет блокировку;</li>
              <li>компании делят общую скорость канала (~360 сообщений в час на номер) и общую очередь.</li>
            </ul>
            <label className="flex items-start gap-2.5 cursor-pointer mt-3">
              <input
                type="checkbox"
                className="w-4 h-4 mt-0.5 rounded accent-gold"
                checked={warningAcknowledged}
                onChange={(e) => setWarningAcknowledged(e.target.checked)}
              />
              <span className="text-sm text-warning">Понимаю риски и хочу назначить эту компанию на канал</span>
            </label>
          </div>
        )}

        {mut.isError && <p className="text-sm text-danger">{getNotificationErrorMessage(mut.error)}</p>}

        <div className="flex gap-3 pt-1">
          <Button variant="secondary" className="flex-1" onClick={onClose}>
            Отмена
          </Button>
          <Button
            className="flex-1"
            disabled={!companyId || (needsWarning && !warningAcknowledged)}
            loading={mut.isPending}
            onClick={() => mut.mutate()}
          >
            Назначить
          </Button>
        </div>
      </div>
    </Modal>
  )
}
