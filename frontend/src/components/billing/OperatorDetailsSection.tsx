import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { billingApi } from '../../api/billing'
import { getOperatorDetailsErrorMessage } from '../../utils/healthConsentError'
import { Card } from '../ui/Card'
import { Button } from '../ui/Button'
import { Input } from '../ui/Input'
import { Icon } from '../ui/Icon'

/**
 * API_CONTRACT_CYCLE20.md §432.8 (Т20-04 п. 3, US-20-01) — held-account operator-of-record details
 * for the paper health-consent form. Deliberately optional everywhere in this component: the form
 * prints fine without any of these (a blank line is printed instead, §432.4), so nothing here is
 * required, and there is no validation gate on save beyond the server's own 400 (ФИО без инициалов,
 * ИНН формата). Lives on "Ваша подписка" because the account HOLDER is who fills it in — never the
 * company's staff, who have no route to this screen at all (`/billing` is CompanyOwner/SuperAdmin only).
 */
export function OperatorDetailsSection() {
  const qc = useQueryClient()
  const { data, isLoading, isError } = useQuery({ queryKey: ['billing-operator-details'], queryFn: billingApi.getOperatorDetails })
  const [editing, setEditing] = useState(false)
  const [fullName, setFullName] = useState('')
  const [address, setAddress] = useState('')
  const [inn, setInn] = useState('')
  const [error, setError] = useState('')

  useEffect(() => {
    if (data) {
      setFullName(data.fullName ?? '')
      setAddress(data.address ?? '')
      setInn(data.inn ?? '')
    }
  }, [data])

  const saveMut = useMutation({
    mutationFn: () => billingApi.updateOperatorDetails({ fullName: fullName.trim() || null, address: address.trim() || null, inn: inn.trim() || null }),
    onSuccess: () => {
      setError('')
      setEditing(false)
      qc.invalidateQueries({ queryKey: ['billing-operator-details'] })
    },
    onError: (err: unknown) => setError(getOperatorDetailsErrorMessage(err)),
  })

  // 404 = no billing account at all (same "not an error state" convention as the subscription/trial
  // queries elsewhere in this file) — nothing to configure yet, so the section just doesn't render.
  if (isError || (!isLoading && !data)) return null

  return (
    <Card className="p-[26px] mb-[18px]">
      <div className="flex items-center justify-between mb-1">
        <h2 className="text-[15.5px] font-semibold text-ink">Реквизиты для бланка согласия на сведения о здоровье</h2>
        {!editing && !isLoading && (
          <Button variant="secondary" size="sm" onClick={() => setEditing(true)}>
            {data?.missing ? 'Заполнить' : 'Изменить'}
          </Button>
        )}
      </div>
      <p className="text-xs text-muted mb-4">
        Сведения об операторе персональных данных, которые печатаются в бланке письменного согласия (§432.4). Заполнять
        не обязательно — бланк печатается и без них, а незаполненные строки останутся линией для заполнения от руки.
      </p>

      {isLoading ? (
        <div className="h-16 bg-cream-deep rounded-xl animate-pulse" />
      ) : editing ? (
        <div className="flex flex-col gap-3">
          <Input label="ФИО оператора (полностью, без инициалов)" value={fullName} onChange={(e) => setFullName(e.target.value)} placeholder="Иванова Мария Сергеевна" />
          <Input label="Адрес" value={address} onChange={(e) => setAddress(e.target.value)} placeholder="г. Барнаул, ул. Ленина, 1" />
          <Input label="ИНН (10 или 12 цифр)" value={inn} onChange={(e) => setInn(e.target.value)} placeholder="222500000000" />
          {error && <p className="text-sm text-danger">{error}</p>}
          <div className="flex gap-3 pt-1">
            <Button
              variant="secondary"
              className="flex-1"
              onClick={() => {
                setEditing(false)
                setError('')
                setFullName(data?.fullName ?? '')
                setAddress(data?.address ?? '')
                setInn(data?.inn ?? '')
              }}
            >
              Отмена
            </Button>
            <Button className="flex-1" loading={saveMut.isPending} onClick={() => saveMut.mutate()}>
              Сохранить
            </Button>
          </div>
        </div>
      ) : data?.missing ? (
        <p className="text-sm text-warning bg-warning-bg rounded-xl px-4 py-2.5 flex items-center gap-2">
          <Icon name="alert-circle" size={14} strokeWidth={1.8} />
          Реквизиты не заполнены — в бланке будет напечатана линия для заполнения от руки.
        </p>
      ) : (
        <div className="text-sm text-ink-soft flex flex-col gap-0.5">
          <p>{data?.fullName}</p>
          <p>{data?.address}</p>
          {data?.inn && <p>ИНН {data.inn}</p>}
        </div>
      )}
    </Card>
  )
}
