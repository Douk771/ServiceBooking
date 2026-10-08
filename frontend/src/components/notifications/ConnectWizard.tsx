import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { NUMBERS_OVERVIEW_QUERY_KEY, type NumbersOverviewDto, type TransportNumbersDto } from '../../api/notificationNumbers'
import { Modal } from '../ui/Modal'
import { Button } from '../ui/Button'
import { Icon } from '../ui/Icon'
import type { WizardStep } from '../../utils/channelRules'
import { TermsStep } from './TermsStep'
import { QrStep } from './QrStep'
import * as T from './numbersTexts'

interface Props {
  transport: TransportNumbersDto
  overview: NumbersOverviewDto
  onClose: () => void
}

/**
 * Мастер подключения номера (US-04): шаги Payment / Terms / Qr / Done (+ PaymentPending, Unavailable, None как
 * информационные). Стартовый шаг — `transport.wizardStep` с сервера (§40.6.3), фронт его не вычисляет; дальше шаг ведут
 * ответы запросов. Любое закрытие перечитывает overview.
 */
export function ConnectWizard({ transport, overview, onClose }: Props) {
  const qc = useQueryClient()
  const [local, setLocal] = useState<WizardStep | null>(null)
  const [createdChannelId, setCreatedChannelId] = useState<string | null>(null)

  const step: WizardStep = local ?? transport.wizardStep
  const channelId = createdChannelId ?? transport.channel?.id ?? null
  const m = transport.displayName
  const refresh = () => qc.invalidateQueries({ queryKey: NUMBERS_OVERVIEW_QUERY_KEY })
  const close = () => {
    void refresh()
    onClose()
  }

  return (
    <Modal title={T.WIZARD_TITLES[step](m)} onClose={close}>
      {(step === 'Payment' || step === 'Terms') && (
        <TermsStep
          transport={transport}
          overview={overview}
          paymentRequest={step === 'Payment'}
          onCancel={close}
          onSubmitted={(ch) => {
            setCreatedChannelId(ch.id)
            void refresh()
            setLocal(step === 'Payment' ? 'PaymentPending' : 'Qr')
          }}
        />
      )}

      {step === 'PaymentPending' && <Notice text={T.PAYMENT_PENDING_TEXT} onClose={close} />}
      {step === 'Unavailable' && <Notice text={transport.unavailableText ?? transport.displayText ?? ''} onClose={close} />}
      {step === 'None' && <Notice text={transport.channel?.displayText ?? transport.displayText ?? ''} onClose={close} />}

      {step === 'Qr' &&
        (channelId ? (
          <QrStep
            channelId={channelId}
            displayName={m}
            instruction={transport.qrInstruction}
            connectionNotice={transport.connectionNotice}
            onConnected={() => {
              void refresh()
              setLocal('Done')
            }}
          />
        ) : (
          <Notice text={transport.displayText ?? ''} onClose={close} />
        ))}

      {step === 'Done' && (
        <div className="flex flex-col items-center gap-3 text-center py-2">
          <Icon name="check-circle" size={32} strokeWidth={1.6} className="text-success" />
          <p className="font-semibold text-ink">{T.DONE_TITLE}</p>
          {transport.channel?.displayText && <p className="text-sm text-ink-soft">{transport.channel.displayText}</p>}
          <Button onClick={close}>{T.DONE_BUTTON}</Button>
        </div>
      )}
    </Modal>
  )
}

function Notice({ text, onClose }: { text: string; onClose: () => void }) {
  return (
    <div className="flex flex-col gap-4 items-start">
      {text && <p className="text-sm text-ink-soft">{text}</p>}
      <Button variant="secondary" onClick={onClose}>
        {T.CLOSE}
      </Button>
    </div>
  )
}
