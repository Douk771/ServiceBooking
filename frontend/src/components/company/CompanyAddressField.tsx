import { useId, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { companyAddressApi } from '../../api/companyAddress'
import { PublicAddressNotice } from './PublicAddressNotice'
import { Input } from '../ui/Input'
import { Button } from '../ui/Button'
import type { Company } from '../../types'

interface Props {
  companyId: string
  initialAddress: string
  /** Fires with the fresh `CompanyDto` after a successful save, so the caller can refresh its cache. */
  onSaved: (company: Company) => void
}

/**
 * ARCHITECTURE_CYCLE19.md §388.3 (formerly `AddressVerifyField`, cycle 13). Automatic address checking is gone —
 * this is an ordinary address field with one obligation left over from cycle 13: the public-address
 * notice gate (§220/§242 unchanged) fires on every save, not just the first.
 */
export function CompanyAddressField({ companyId, initialAddress, onSaved }: Props) {
  const inputId = useId()
  const [value, setValue] = useState(initialAddress)
  const [dirty, setDirty] = useState(false)
  const [showNotice, setShowNotice] = useState(false)
  const [saveError, setSaveError] = useState('')
  const errorId = `${inputId}-error`

  const saveMut = useMutation({
    mutationFn: () => companyAddressApi.saveAddress(companyId, value),
    onSuccess: (result) => {
      setSaveError('')
      setDirty(false)
      onSaved(result.company)
    },
    onError: () => setSaveError('Не удалось сохранить адрес. Попробуйте ещё раз.'),
  })

  function handleChange(v: string) {
    setValue(v)
    setDirty(v !== initialAddress)
    setSaveError('')
  }

  return (
    <div className="flex flex-col gap-2">
      <Input
        id={inputId}
        label="Адрес"
        value={value}
        onChange={(e) => handleChange(e.target.value)}
        aria-describedby={saveError ? errorId : undefined}
      />

      {dirty && (
        <div>
          <Button type="button" size="sm" loading={saveMut.isPending} onClick={() => setShowNotice(true)}>
            Сохранить
          </Button>
        </div>
      )}

      {saveError && (
        <p id={errorId} className="text-xs text-danger">
          {saveError}
        </p>
      )}

      {showNotice && (
        <PublicAddressNotice
          onConfirmed={() => {
            setShowNotice(false)
            // An explicit Save press is the only moment the address is actually written — §209.
            saveMut.mutate()
          }}
          onCancel={() => setShowNotice(false)}
        />
      )}
    </div>
  )
}
