import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { menuApi } from '../../api/menu'
import { WeekdayPicker } from './WeekdayPicker'
import { InlineError } from '../StatePanels'
import { getCatalogErrorMessage } from '../../utils/catalogError'
import { ALL_WEEKDAYS } from '../../utils/weekdays'
import type { CategoryDto, DayOfWeek } from '../../types'

/** US-24-10 (P1) — «применить ко всей категории»: sets the weekdays of EVERY product in the category at once. */
export function CategoryWeekdaysModal({ shopId, category, onClose, onSaved }: { shopId: string; category: CategoryDto; onClose: () => void; onSaved: () => void }) {
  const [days, setDays] = useState<DayOfWeek[]>([...ALL_WEEKDAYS])
  const save = useMutation({ mutationFn: () => menuApi.setCategoryWeekdays(shopId, category.id, days), onSuccess: onSaved })
  return (
    <Modal title={`Дни продажи — «${category.name}»`} onClose={onClose} dismissible={!save.isPending}>
      <p className="text-sm text-ink-soft mb-4">Выбранные дни получат все товары категории. Отдельные товары потом можно поправить.</p>
      <WeekdayPicker legend="Продаётся по дням" value={days} onChange={setDays} hint="Без отметок товары продаются только по меню на дату." />
      {save.isError && (
        <div className="mt-3">
          <InlineError>{getCatalogErrorMessage(save.error, 'Не удалось сохранить дни недели.')}</InlineError>
        </div>
      )}
      <div className="flex gap-3 mt-5">
        <Button variant="secondary" className="flex-1" onClick={onClose} disabled={save.isPending}>
          Отмена
        </Button>
        <Button className="flex-1" loading={save.isPending} onClick={() => save.mutate()}>
          Применить ко всей категории
        </Button>
      </div>
    </Modal>
  )
}
