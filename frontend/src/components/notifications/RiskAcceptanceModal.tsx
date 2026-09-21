import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { notificationChannelsApi } from '../../api/notificationChannels'
import { Modal } from '../ui/Modal'
import { Button } from '../ui/Button'
import { getNotificationErrorMessage } from '../../utils/notificationError'

interface Props {
  channelId: string
  riskTextVersion: string
  onClose: () => void
  onAccepted: () => void
}

/**
 * US-53 п. 7 — a separate, deliberate action before connecting, not a checkbox buried in the offer.
 * Texts are a "рыба" pending legal sign-off (SPEC §4.1 п. 7) — kept here, not fetched from the
 * server, since the contract only sends the version string to pin what was shown (§23).
 */
export function RiskAcceptanceModal({ channelId, riskTextVersion, onClose, onAccepted }: Props) {
  const [checked, setChecked] = useState(false)
  const qc = useQueryClient()

  const mut = useMutation({
    mutationFn: () => notificationChannelsApi.acceptRisk(channelId, riskTextVersion),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['notification-channels'] })
      onAccepted()
    },
  })

  return (
    <Modal title="Прежде чем подключить номер" onClose={onClose}>
      <div className="flex flex-col gap-4">
        <div className="text-sm text-ink-soft flex flex-col gap-2.5">
          <p>Подключая номер WhatsApp, вы понимаете и соглашаетесь:</p>
          <ul className="list-disc pl-5 flex flex-col gap-2">
            <li>
              сообщения клиентам уходят <strong>от вашего имени и с вашего номера</strong> — ответственность за
              информирование клиентов через этот мессенджер несёте вы (41-ФЗ);
            </li>
            <li>
              доставка сообщений <strong>не гарантирована</strong> из-за ограничений доступа к WhatsApp в РФ;
            </li>
            <li>
              подключение выполняется через <strong>неофициальный шлюз</strong>, что противоречит правилам WhatsApp:
              автоматическая отправка может привести к <strong>невосстановимой блокировке</strong> этого номера в
              WhatsApp вместе с перепиской. Платформа снижает этот риск (пауза между сообщениями, автоматическое
              гашение канала при сбое), но не устраняет его полностью;
            </li>
            <li>
              при блокировке номера <strong>деньги за оплаченный период не возвращаются</strong>, но вы сможете
              привязать другой номер в этом же периоде без повторной оплаты.
            </li>
          </ul>
          <p className="text-xs text-muted">
            Рекомендуем использовать отдельный рабочий номер салона, а не личный номер. Версия текста:{' '}
            {riskTextVersion}.
          </p>
        </div>

        <label className="flex items-start gap-2.5 cursor-pointer">
          <input
            type="checkbox"
            className="w-4 h-4 mt-0.5 rounded accent-gold"
            checked={checked}
            onChange={(e) => setChecked(e.target.checked)}
          />
          <span className="text-sm text-ink-soft">Я прочитал(а) и принимаю условия подключения канала</span>
        </label>

        {mut.isError && <p className="text-sm text-danger">{getNotificationErrorMessage(mut.error)}</p>}

        <div className="flex gap-3 pt-1">
          <Button variant="secondary" className="flex-1" onClick={onClose}>
            Отмена
          </Button>
          <Button className="flex-1" disabled={!checked} loading={mut.isPending} onClick={() => mut.mutate()}>
            Принимаю
          </Button>
        </div>
      </div>
    </Modal>
  )
}
