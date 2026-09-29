import { useRef, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { catalogApi } from '../../api/catalog'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { getUploadErrorMessage } from '@/utils/uploadError'
import { InlineError } from '../StatePanels'
import { WeekdayPicker } from './WeekdayPicker'
import { ALL_WEEKDAYS, normalizeWeekdays } from '../../utils/weekdays'
import { getCatalogErrorMessage } from '../../utils/catalogError'
import { parsePrice, validateProduct } from '../../utils/productForm'
import type { CategoryDto, DayOfWeek, ProductDto, ProductInput, ProductUnit } from '../../types'

const DEFAULT_STEP = 100

interface Props {
  shopId: string
  categories: CategoryDto[]
  product?: ProductDto
  defaultCategoryId?: string | null
  onClose: () => void
  /** Called after every server-side change (save, photo) so the list behind the dialog refreshes. */
  onSaved: (p: ProductDto) => void
}

/** US-23-15 — create/edit a product. The unit (штучный/весовой) is fixed after creation (`UnitChangeNotAllowed`). */
export function ProductModal({ shopId, categories, product, defaultCategoryId = null, onClose, onSaved }: Props) {
  // A product created in this dialog whose photo failed to upload: the dialog stays open on that product,
  // so a retry edits it instead of creating a duplicate.
  const [created, setCreated] = useState<ProductDto | undefined>(undefined)
  const target = product ?? created
  const editing = !!target
  const [name, setName] = useState(product?.name ?? '')
  const [categoryId, setCategoryId] = useState<string>(product ? (product.categoryId ?? '') : (defaultCategoryId ?? ''))
  const [description, setDescription] = useState(product?.description ?? '')
  const [unit, setUnit] = useState<ProductUnit>(product?.unit ?? 'Piece')
  const [price, setPrice] = useState(product ? String(product.price).replace('.', ',') : '')
  const [portionText, setPortionText] = useState(product?.portionText ?? '')
  const [step, setStep] = useState(String(product?.weightStepGrams ?? DEFAULT_STEP))
  const [min, setMin] = useState(product && product.unit === 'Weight' ? String(product.minQuantity ?? '') : '')
  const [isPublished, setIsPublished] = useState(product?.isPublished ?? true)
  const [composition, setComposition] = useState(product?.foodInfo.compositionAndAllergens ?? '')
  const [weekdays, setWeekdays] = useState<DayOfWeek[]>(product?.availableWeekdays ? normalizeWeekdays(product.availableWeekdays) : [...ALL_WEEKDAYS])
  const [formError, setFormError] = useState('')
  const [pendingFile, setPendingFile] = useState<File | null>(null)
  const [current, setCurrent] = useState<ProductDto | undefined>(product)
  const [imageError, setImageError] = useState('')
  const fileRef = useRef<HTMLInputElement>(null)

  const save = useMutation({
    mutationFn: async () => {
      const body: ProductInput = {
        categoryId: categoryId || null,
        name: name.trim(),
        description: description.trim() || null,
        unit,
        price: parsePrice(price)!,
        portionText: unit === 'Piece' ? portionText.trim() || null : null,
        weightStepGrams: unit === 'Weight' ? Number(step) : null,
        minQuantityGrams: unit === 'Weight' && min.trim() !== '' ? Number(min) : null,
        isPublished,
        foodInfo: { compositionAndAllergens: composition.trim() || null },
        availableWeekdays: weekdays,
      }
      let saved = target ? await catalogApi.updateProduct(shopId, target.id, body) : await catalogApi.createProduct(shopId, body)
      let photoFailed = false
      // A photo picked while creating is uploaded right after the product exists; its failure must not
      // undo the creation — the product is saved and the photo can be retried from this same dialog.
      if (!target && pendingFile) {
        try {
          saved = await catalogApi.uploadImage(shopId, saved.id, pendingFile)
        } catch (e) {
          photoFailed = true
          setImageError(getUploadErrorMessage(e))
        }
      }
      return { saved, photoFailed }
    },
    onSuccess: ({ saved, photoFailed }) => {
      onSaved(saved)
      setCurrent(saved)
      if (photoFailed) {
        setCreated(saved)
        setPendingFile(null)
      } else {
        onClose()
      }
    },
  })

  const image = useMutation({
    mutationFn: (file: File) => catalogApi.uploadImage(shopId, target!.id, file),
    onMutate: () => setImageError(''),
    onSuccess: (p) => {
      setCurrent(p)
      onSaved(p)
    },
    onError: (e) => setImageError(getUploadErrorMessage(e)),
  })
  const removeImage = useMutation({
    mutationFn: () => catalogApi.deleteImage(shopId, target!.id),
    onMutate: () => setImageError(''),
    onSuccess: (p) => {
      setCurrent(p)
      onSaved(p)
    },
    onError: (e) => setImageError(getUploadErrorMessage(e)),
  })

  const submit = (e: React.FormEvent) => {
    e.preventDefault()
    const problem = validateProduct({ name, price, unit, step, min })
    if (problem) {
      setFormError(problem)
      return
    }
    setFormError('')
    save.mutate()
  }

  const busy = save.isPending || image.isPending || removeImage.isPending

  return (
    <Modal title={product ? 'Изменить товар' : 'Новый товар'} onClose={onClose} dismissible={!busy}>
      <form onSubmit={submit} className="flex flex-col gap-4" noValidate>
        <Input label="Название *" value={name} maxLength={200} onChange={(e) => setName(e.target.value)} />

        <div className="flex flex-col gap-1.5">
          <label htmlFor="product-category" className="text-[13px] font-medium text-[#4A4038]">Категория</label>
          <select id="product-category" value={categoryId} onChange={(e) => setCategoryId(e.target.value)} className="rounded-xl border border-line px-4 py-3 text-sm bg-white text-ink outline-none focus:border-gold">
            <option value="">Без категории («Другое»)</option>
            {categories.map((c) => (
              <option key={c.id} value={c.id}>{c.name}</option>
            ))}
          </select>
        </div>

        <fieldset>
          <legend className="text-[13px] font-medium text-[#4A4038] mb-2">Тип товара</legend>
          <div className="flex gap-2">
            {([['Piece', 'Штучный'], ['Weight', 'Весовой']] as const).map(([v, label]) => (
              <label key={v} className={`flex-1 rounded-xl border px-4 py-2.5 text-sm ${unit === v ? 'border-gold bg-cream-deep/50 font-semibold' : 'border-line'} ${editing ? 'opacity-70' : 'cursor-pointer'}`}>
                <input type="radio" name="unit" className="sr-only" checked={unit === v} disabled={editing} onChange={() => setUnit(v)} />
                {label}
              </label>
            ))}
          </div>
          {editing && <p className="text-xs text-muted mt-1.5">Тип менять нельзя — заведите новый товар.</p>}
        </fieldset>

        <Input
          label={unit === 'Weight' ? 'Цена за 1 кг, ₽ *' : 'Цена за штуку, ₽ *'}
          inputMode="decimal"
          value={price}
          onChange={(e) => setPrice(e.target.value)}
          placeholder="250"
        />

        {unit === 'Piece' ? (
          <Input label="Вес или объём порции" value={portionText} maxLength={50} onChange={(e) => setPortionText(e.target.value)} placeholder="300 г" />
        ) : (
          <div className="grid grid-cols-2 gap-4">
            <Input label="Шаг заказа, г" inputMode="numeric" value={step} onChange={(e) => setStep(e.target.value)} />
            <Input label="Минимум, г" inputMode="numeric" value={min} onChange={(e) => setMin(e.target.value)} placeholder={`по умолчанию ${step || DEFAULT_STEP}`} />
          </div>
        )}
        {unit === 'Weight' && <p className="text-xs text-muted -mt-2">Покупатель выбирает вес кратно шагу, не больше 10 кг на позицию.</p>}

        <div className="flex flex-col gap-1.5">
          <label htmlFor="product-description" className="text-[13px] font-medium text-[#4A4038]">Описание</label>
          <textarea id="product-description" rows={2} value={description} onChange={(e) => setDescription(e.target.value)} className="rounded-xl border border-line px-4 py-3 text-sm outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep resize-none bg-white text-ink" />
        </div>

        <div className="flex flex-col gap-1.5">
          <label htmlFor="product-composition" className="text-[13px] font-medium text-[#4A4038]">Состав и аллергены</label>
          <textarea id="product-composition" rows={2} maxLength={2000} value={composition} onChange={(e) => setComposition(e.target.value)} className="rounded-xl border border-line px-4 py-3 text-sm outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep resize-none bg-white text-ink" />
        </div>

        <div>
          <p className="text-[13px] font-medium text-[#4A4038] mb-2">Фото</p>
          <input
            ref={fileRef}
            type="file"
            accept="image/jpeg,image/png,image/webp"
            className="hidden"
            aria-label="Файл фото товара"
            onChange={(e) => {
              const f = e.target.files?.[0]
              e.target.value = ''
              if (!f) return
              if (editing) image.mutate(f)
              else setPendingFile(f)
            }}
          />
          <div className="flex items-center gap-3 flex-wrap">
            {current?.thumbnailUrl && <img src={current.thumbnailUrl} alt="" className="w-16 h-16 rounded-xl object-cover" />}
            <Button type="button" variant="secondary" size="sm" loading={image.isPending} onClick={() => fileRef.current?.click()}>
              {current?.imageUrl || pendingFile ? 'Заменить фото' : 'Загрузить фото'}
            </Button>
            {current?.imageUrl && (
              <Button type="button" variant="ghost" size="sm" loading={removeImage.isPending} onClick={() => removeImage.mutate()}>
                Убрать фото
              </Button>
            )}
            {pendingFile && !editing && <span className="text-xs text-ink-soft">Выбрано: {pendingFile.name}</span>}
          </div>
          {imageError && <p role="alert" className="text-xs text-danger mt-1.5">{imageError}</p>}
        </div>

        <WeekdayPicker legend="Дни продажи" value={weekdays} onChange={setWeekdays} hint="Без отметок товар продаётся только по меню на дату." />

        <label className="flex items-center gap-3 text-sm text-ink cursor-pointer">
          <input type="checkbox" className="w-4 h-4 accent-gold" checked={isPublished} onChange={(e) => setIsPublished(e.target.checked)} />
          Опубликован — виден покупателям
        </label>

        {formError && <InlineError>{formError}</InlineError>}
        {save.isError && <InlineError>{getCatalogErrorMessage(save.error, 'Не удалось сохранить товар.')}</InlineError>}
        {created && imageError && <InlineError>Товар сохранён, но фото не загрузилось — выберите файл ещё раз или закройте окно.</InlineError>}

        <div className="flex gap-3 pt-1">
          <Button type="button" variant="secondary" className="flex-1" onClick={onClose} disabled={busy}>Отмена</Button>
          <Button type="submit" className="flex-1" loading={save.isPending} disabled={image.isPending || removeImage.isPending}>Сохранить</Button>
        </div>
      </form>
    </Modal>
  )
}
