import { useId, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { companyAddressApi } from '../../api/companyAddress'
import { PublicAddressNotice } from './PublicAddressNotice'
import { Input } from '../ui/Input'
import { Button } from '../ui/Button'
import type {
  AddressCandidateDto,
  AddressLookupResultDto,
  Company,
  CompanyAddressVerificationDto,
  CompanyAddressVerificationResultDto,
} from '../../types'

// §237 — the server-composed copy for `verification.outcome`, shown next to the field after a save.
// `Ok`/`Disabled` need no extra line here: `Ok` is covered by the "Подтверждён по карте …" status
// derived from `addressVerification` once the parent refetches, and `Disabled` means the switch is
// off, i.e. this whole block is never reached (`available` is false).
const OUTCOME_MESSAGE: Partial<Record<CompanyAddressVerificationResultDto['outcome'], string>> = {
  Empty: 'Карта не нашла такой адрес. Адрес сохранён, но не подтверждён.',
  Unavailable: 'Карта временно недоступна. Адрес сохранён, но не подтверждён — можно повторить проверку позже.',
}

interface Props {
  companyId: string
  initialAddress: string
  cityId?: number | null
  /** `company.addressVerification` — `null`/absent means "not available for this caller", handled
   *  identically to `available === false` per §211/§237. */
  addressVerification?: CompanyAddressVerificationDto | null
  /** Fires with the fresh `CompanyDto` after a successful save, so the caller can refresh its cache. */
  onSaved: (company: Company) => void
}

/**
 * ARCHITECTURE_CYCLE13.md §211/§209 (US-134…US-138). Owns its own save action rather than joining the
 * surrounding settings form's submit — §209 requires address writes to go through their own endpoint
 * (`PUT /api/companies/{id}/address`) specifically so the geocoder is never dialled as a side effect
 * of saving an unrelated field.
 *
 * The public-address-notice gate (§220) applies to EVERY save from this field, `available` or not —
 * it is the one part of this cycle that is obligatory in the switch's default ("logging") state.
 */
export function AddressVerifyField({ companyId, initialAddress, cityId, addressVerification, onSaved }: Props) {
  const inputId = useId()
  const [value, setValue] = useState(initialAddress)
  const [dirty, setDirty] = useState(false)
  const [showNotice, setShowNotice] = useState(false)
  const [lookupResult, setLookupResult] = useState<AddressLookupResultDto | null>(null)
  const [saveVerification, setSaveVerification] = useState<CompanyAddressVerificationResultDto | null>(null)
  const [saveError, setSaveError] = useState('')

  // §237 — the ONLY correct source for "show the verify button": `company.addressVerification
  // ?.available === true`. Absent/`null` (rubilnik off, or this caller doesn't manage the company)
  // means "ordinary address field, as before the cycle" — no button, no status, no candidates.
  const available = addressVerification?.available === true
  const warningsId = `${inputId}-warnings`
  const saveWarningsId = `${inputId}-save-warnings`
  const describedBy =
    [
      lookupResult && lookupResult.warnings.length > 0 ? warningsId : null,
      saveVerification && saveVerification.warnings.length > 0 ? saveWarningsId : null,
    ]
      .filter(Boolean)
      .join(' ') || undefined

  const lookupMut = useMutation({
    mutationFn: () => companyAddressApi.lookup({ address: value, cityId, companyId }),
    onSuccess: (r) => {
      setSaveVerification(null)
      setLookupResult(r)
    },
  })

  const saveMut = useMutation({
    mutationFn: (verify: boolean) => companyAddressApi.saveAddress(companyId, value, verify),
    onSuccess: (result) => {
      setSaveError('')
      setDirty(false)
      setLookupResult(null)
      // §237 — `verification.outcome` + `verification.warnings[].message` is what tells the owner
      // WHY the save didn't end in "Подтверждён": surface it here rather than discarding it, since
      // `addressVerification` on the refreshed `company` only carries the terminal status/precision,
      // not the per-attempt outcome or warnings that produced it.
      setSaveVerification(result.verification)
      onSaved(result.company)
    },
    // §234 — this call never 4xx/5xx's on the map being unreachable (`outcome: "Unavailable"` in a
    // 200). A save actually failing here means something else went wrong (network, auth, 429).
    onError: () => setSaveError('Не удалось сохранить адрес. Попробуйте ещё раз.'),
  })

  function handleChange(v: string) {
    setValue(v)
    setDirty(v !== initialAddress)
    setLookupResult(null)
    setSaveVerification(null)
    setSaveError('')
  }

  function pickCandidate(c: AddressCandidateDto) {
    // §233 — substituting the map's wording into OUR field is a client-side action; nothing is saved
    // until the human presses Save below, and what gets saved is this field's text, not the map's.
    // Unlike free typing, this does NOT clear `lookupResult` — §211 shows the attribution "под
    // списком вариантов, пока варианты на экране", and picking one is how a person actually reads it.
    setValue(c.formattedAddress)
    setDirty(c.formattedAddress !== initialAddress)
    setSaveVerification(null)
    setSaveError('')
  }

  return (
    <div className="flex flex-col gap-2">
      <Input
        id={inputId}
        label="Адрес"
        value={value}
        onChange={(e) => handleChange(e.target.value)}
        aria-describedby={describedBy}
      />

      {available && !dirty && (
        <p className="text-xs text-muted">
          {addressVerification?.status === 'Verified' && addressVerification.verifiedAt
            ? `Подтверждён по карте ${new Date(addressVerification.verifiedAt).toLocaleDateString('ru-RU')}`
            : 'Не подтверждён'}
        </p>
      )}

      <div className="flex items-center gap-2 flex-wrap">
        {available && (
          <Button
            type="button"
            variant="secondary"
            size="sm"
            loading={lookupMut.isPending}
            disabled={!value.trim()}
            onClick={() => lookupMut.mutate()}
          >
            Проверить адрес
          </Button>
        )}
        {/* Also offered when the address text hasn't changed but the last save didn't confirm it
            (`Empty`/`Unavailable`) — otherwise there is no way to retry a lookup-less save once the
            field settles back to `initialAddress` (review finding, cycle 13). */}
        {(dirty || (saveVerification && saveVerification.outcome !== 'Ok')) && (
          <Button type="button" size="sm" loading={saveMut.isPending} onClick={() => setShowNotice(true)}>
            {dirty ? 'Сохранить' : 'Повторить проверку'}
          </Button>
        )}
      </div>

      {lookupResult && (
        // §236 — the server composes `warnings[].message` (below); this component doesn't invent a
        // second wording for `outcome: "Empty"`/`"Unavailable"` on top of it (that's exactly the
        // "second version of the copy drifts from the server's" trap the convention exists to avoid).
        <div className="rounded-xl border border-line bg-cream-deep/40 p-3 flex flex-col gap-2 text-xs">
          {lookupResult.candidates.length > 0 && (
            <ul className="flex flex-col gap-2">
              {lookupResult.candidates.map((c, i) => (
                <li key={i} className="flex flex-col gap-0.5">
                  <button
                    type="button"
                    className="text-left text-gold-dark hover:text-gold-darker hover:underline"
                    onClick={() => pickCandidate(c)}
                  >
                    {c.formattedAddress}
                  </button>
                  {/* Per-candidate warnings (precision, city mismatch) — separate from the top-level
                      `lookupResult.warnings` below, which cover the request as a whole. */}
                  {c.warnings.length > 0 && (
                    <ul className="flex flex-col gap-0.5 text-warning">
                      {c.warnings.map((w, wi) => (
                        <li key={wi}>{w.message}</li>
                      ))}
                    </ul>
                  )}
                </li>
              ))}
            </ul>
          )}
          {lookupResult.warnings.length > 0 && (
            <ul id={warningsId} className="flex flex-col gap-1 text-warning">
              {lookupResult.warnings.map((w, i) => (
                <li key={i}>{w.message}</li>
              ))}
            </ul>
          )}
          {lookupResult.candidates.length > 0 && lookupResult.attribution && (
            <p className="text-muted">{lookupResult.attribution}</p>
          )}
        </div>
      )}

      {/* §237 — what the save actually resulted in: for `Empty`/`Unavailable` this is the only place
          the owner learns the address was saved but not confirmed, and why. `Ok` needs no extra line
          (covered by the "Подтверждён по карте …" status above once the parent refetches); `Disabled`
          can't happen here (`available` gates this whole block). */}
      {saveVerification && OUTCOME_MESSAGE[saveVerification.outcome] && (
        <p className="text-xs text-warning">{OUTCOME_MESSAGE[saveVerification.outcome]}</p>
      )}
      {saveVerification && saveVerification.warnings.length > 0 && (
        <ul id={saveWarningsId} className="flex flex-col gap-1 text-xs text-warning">
          {saveVerification.warnings.map((w, i) => (
            <li key={i}>{w.message}</li>
          ))}
        </ul>
      )}

      {saveError && <p className="text-xs text-danger">{saveError}</p>}

      {showNotice && (
        <PublicAddressNotice
          onConfirmed={() => {
            setShowNotice(false)
            // An explicit Save press is exactly the "only on button press" moment §209 requires
            // before the server is allowed to dial the geocoder — attempted whenever the switch is
            // on, regardless of whether the owner used "Проверить адрес" first.
            saveMut.mutate(available)
          }}
          onCancel={() => setShowNotice(false)}
        />
      )}
    </div>
  )
}
