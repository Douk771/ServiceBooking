import { useState } from 'react'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { RadioChips } from '../pickup/RadioChips'
import type { ProductDto, SoldOutScope } from '../../types'

interface Props {
  product: ProductDto
  busy: boolean
  onClose: () => void
  onConfirm: (scope: SoldOutScope) => void
}

/** US-24-13 — «Закончилось»: on today only (default) or until cancelled. Goods always sends `scope` (API_CONTRACT_CYCLE24.md §482.2). */
export function SoldOutDialog({ product, busy, onClose, onConfirm }: Props) {
  const [scope, setScope] = useState<SoldOutScope>('Today')
  return (
    <Modal title={`«${product.name}» закончилось`} onClose={onClose} dismissible={!busy}>
      <p className="text-sm text-ink-soft mb-4">На какой срок убрать товар из продажи?</p>
      <RadioChips<SoldOutScope>
        label="Срок"
        value={scope}
        onChange={setScope}
        options={[
          { value: 'Today', label: 'Нет на сегодня' },
          { value: 'UntilCancelled', label: 'Нет до отмены' },
        ]}
      />
      <p className="text-xs text-muted mt-2">
        {scope === 'Today' ? 'Завтра товар вернётся в продажу сам.' : 'Товар вернётся, когда вы сами нажмёте «Вернуть в продажу».'}
      </p>
      <div className="flex gap-3 mt-5">
        <Button variant="secondary" className="flex-1" onClick={onClose} disabled={busy}>
          Отмена
        </Button>
        <Button className="flex-1" loading={busy} onClick={() => onConfirm(scope)}>
          Убрать из продажи
        </Button>
      </div>
    </Modal>
  )
}
