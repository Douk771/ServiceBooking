import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { InlineError } from '@/components/ui/InlineError'
import { Input } from '@/components/ui/Input'
import { useCabinetCompany, useSlotVertical } from '@/components/slots/SlotVerticalContext'
import { can } from '@/utils/slots/slotPermissions'
import { getStayErrorMessage } from '@/utils/slots/slotError'

/** `/cabinet/:companyId/services/new` (`ManageServices`) — only the name; the service starts unpublished with an address from its name. */
export function CabinetServiceCreateView() {
  const { api, paths, NotFound } = useSlotVertical()
  const staysServicesApi = api.cabinet
  const { company } = useCabinetCompany()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const [name, setName] = useState('')
  const [error, setError] = useState('')

  const create = useMutation({
    mutationFn: () => staysServicesApi.create(company.id, name.trim()),
    onSuccess: (s) => {
      void qc.invalidateQueries({ queryKey: ['stays-services', company.id] })
      navigate(paths.cabinetService(company.id, s.id), { replace: true })
    },
    onError: (err) => setError(getStayErrorMessage(err, 'Не удалось создать услугу.')),
  })

  if (!can(company.myPermissions, 'ManageServices')) return <NotFound title="Раздел недоступен" hint="Услуги добавляет владелец." />

  return (
    <main className="mx-auto max-w-[560px] px-4 pb-6 pt-8 sm:px-8">
      <h2 className="font-serif text-[26px] text-ink">Новая услуга</h2>
      <p className="mb-6 mt-1 text-sm text-ink-soft">Сначала название. Расписание, цены, позиции, правила и публикация — на странице услуги.</p>
      <form
        noValidate
        className="flex flex-col gap-5 rounded-3xl border border-line bg-white p-6"
        onSubmit={(e) => {
          e.preventDefault()
          const n = name.trim()
          if (!n || n.length > 100) return setError('Укажите название услуги')
          setError('')
          create.mutate()
        }}
      >
        <Input label="Название услуги *" maxLength={100} value={name} onChange={(e) => setName(e.target.value)} />
        {error && <InlineError>{error}</InlineError>}
        <div className="flex gap-3">
          <Button type="button" variant="secondary" className="min-h-[44px] flex-1" onClick={() => navigate(paths.cabinetServices(company.id))}>
            Отмена
          </Button>
          <Button type="submit" className="min-h-[44px] flex-1" loading={create.isPending}>
            Создать услугу
          </Button>
        </div>
      </form>
    </main>
  )
}
