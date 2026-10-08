import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { InlineError } from '@/components/ui/InlineError'
import { Input } from '@/components/ui/Input'
import { staysHousesApi } from '../../api/staysHouses'
import { useStaysCompany } from '../../hooks/useStaysCompany'
import { can } from '../../utils/permissions'
import { getStayErrorMessage } from '../../utils/stayError'
import { NotFoundPage } from '../NotFoundPage'

/** `/cabinet/:companyId/houses/new` (`ManageHouses`) — name and capacity; the rest on the house page. The house starts unpublished, with a slug from its name. */
export function HouseCreatePage() {
  const { company, refresh } = useStaysCompany()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const [name, setName] = useState('')
  const [capacity, setCapacity] = useState('4')
  const [error, setError] = useState('')

  const create = useMutation({
    mutationFn: () => staysHousesApi.create(company.id, { name: name.trim(), capacity: Number(capacity) }),
    onSuccess: (h) => {
      void qc.invalidateQueries({ queryKey: ['stays-houses', company.id] })
      refresh()
      navigate(`/cabinet/${company.id}/houses/${h.id}`, { replace: true })
    },
    onError: (err) => setError(getStayErrorMessage(err, 'Не удалось создать дом.')),
  })

  if (!can(company.myPermissions, 'ManageHouses')) return <NotFoundPage title="Раздел недоступен" hint="Дома добавляет владелец." />

  const submit = () => {
    const n = name.trim()
    const c = Number(capacity)
    if (!n || n.length > 100) return setError('Укажите название дома')
    if (!Number.isInteger(c) || c < 1 || c > 50) return setError('Вместимость — от 1 до 50')
    setError('')
    create.mutate()
  }

  return (
    <main className="mx-auto max-w-[560px] px-4 pb-6 pt-8 sm:px-8">
      <h2 className="font-serif text-[26px] text-ink">Новый дом</h2>
      <p className="mb-6 mt-1 text-sm text-ink-soft">Сначала название и вместимость. Описание, фото, цены и публикация — на странице дома.</p>
      <form
        noValidate
        className="flex flex-col gap-5 rounded-3xl border border-line bg-white p-6"
        onSubmit={(e) => {
          e.preventDefault()
          submit()
        }}
      >
        <Input label="Название дома *" maxLength={100} value={name} onChange={(e) => setName(e.target.value)} />
        <Input label="Вместимость, гостей *" type="number" inputMode="numeric" min={1} max={50} value={capacity} onChange={(e) => setCapacity(e.target.value)} />
        {error && <InlineError>{error}</InlineError>}
        <div className="flex gap-3">
          <Button type="button" variant="secondary" className="min-h-[44px] flex-1" onClick={() => navigate(`/cabinet/${company.id}/houses`)}>
            Отмена
          </Button>
          <Button type="submit" className="min-h-[44px] flex-1" loading={create.isPending}>
            Создать дом
          </Button>
        </div>
      </form>
    </main>
  )
}
