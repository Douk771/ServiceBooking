import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { catalogApi } from '../../api/catalog'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { InlineError } from '../StatePanels'
import { getCatalogErrorMessage } from '../../utils/catalogError'
import type { CategoryDto } from '../../types'

export function CategoryModal({ shopId, category, onClose, onSaved }: { shopId: string; category?: CategoryDto; onClose: () => void; onSaved: () => void }) {
  const [name, setName] = useState(category?.name ?? '')
  const [isHidden, setIsHidden] = useState(category?.isHidden ?? false)
  const [error, setError] = useState('')

  const save = useMutation({
    mutationFn: () => {
      const body = { name: name.trim(), isHidden }
      return category ? catalogApi.updateCategory(shopId, category.id, body) : catalogApi.createCategory(shopId, body)
    },
    onSuccess: onSaved,
  })

  return (
    <Modal title={category ? 'Изменить категорию' : 'Новая категория'} onClose={onClose} dismissible={!save.isPending}>
      <form
        className="flex flex-col gap-4"
        onSubmit={(e) => {
          e.preventDefault()
          if (!name.trim()) {
            setError('Укажите название категории')
            return
          }
          setError('')
          save.mutate()
        }}
      >
        <Input label="Название *" value={name} maxLength={100} onChange={(e) => setName(e.target.value)} autoFocus />
        <label className="flex items-start gap-3 text-sm text-ink cursor-pointer">
          <input type="checkbox" className="w-4 h-4 mt-0.5 accent-gold" checked={isHidden} onChange={(e) => setIsHidden(e.target.checked)} />
          <span>
            Скрыть категорию
            <span className="block text-xs text-ink-soft">Товары скрытой категории покупатель не видит.</span>
          </span>
        </label>
        {error && <InlineError>{error}</InlineError>}
        {save.isError && <InlineError>{getCatalogErrorMessage(save.error, 'Не удалось сохранить категорию.')}</InlineError>}
        <div className="flex gap-3">
          <Button type="button" variant="secondary" className="flex-1" onClick={onClose} disabled={save.isPending}>Отмена</Button>
          <Button type="submit" className="flex-1" loading={save.isPending}>Сохранить</Button>
        </div>
      </form>
    </Modal>
  )
}
