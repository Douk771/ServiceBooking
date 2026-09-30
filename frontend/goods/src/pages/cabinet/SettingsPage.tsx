import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Input } from '@/components/ui/Input'
import { CompanyPhotosSection } from '@/pages/owner/CompanyPhotosSection'
import { ShopProfileSection } from '../../components/profile/ShopProfileSection'
import { shopsApi } from '../../api/shops'
import { useShopContext } from '../../hooks/useShop'
import { InlineError } from '../../components/StatePanels'
import { CatalogListingSection } from '../../components/CatalogListingSection'
import { getGoodsErrorMessage } from '../../utils/orderError'
import { getCatalogErrorMessage } from '../../utils/catalogError'
import type { OrderAcceptanceMode, SellerInfoInput, ShopCustomerMode, ShopManageDto } from '../../types'

/** US-23-09 / US-23-10 — owner-only: shop profile + logo, ordering rules, seller details [legal L2]. */
export function SettingsPage() {
  const { shop } = useShopContext()
  const qc = useQueryClient()
  return (
    <main className="max-w-[760px] mx-auto px-4 sm:px-8 pt-8 flex flex-col gap-5">
      <ShopProfileSection key={shop.id} shop={shop} />
      <CompanyPhotosSection
        companyId={shop.id}
        kind="shop"
        onChanged={() => void qc.invalidateQueries({ queryKey: ['storefront'] })}
      />
      <RulesSection shop={shop} />
      <CatalogListingSection shopId={shop.id} />
      <SellerSection shop={shop} />
    </main>
  )
}

function useRefreshShop(shopId: string) {
  const qc = useQueryClient()
  return {
    set: (shop: ShopManageDto) => {
      qc.setQueryData(['shop', shopId], shop)
      void qc.invalidateQueries({ queryKey: ['my-shops'] })
    },
    refetch: () => {
      void qc.invalidateQueries({ queryKey: ['shop', shopId] })
      void qc.invalidateQueries({ queryKey: ['my-shops'] })
    },
  }
}

function SavedNote({ show }: { show: boolean }) {
  return show ? (
    <span role="status" className="text-sm text-success font-medium">
      Сохранено
    </span>
  ) : null
}

const customerModes: { value: ShopCustomerMode; title: string; text: string }[] = [
  { value: 'Anyone', title: 'Любой, по имени и телефону', text: 'Гость вводит имя и телефон и проходит проверку «не робот».' },
  { value: 'VerifiedPhoneOnly', title: 'Только с подтверждённым телефоном', text: 'Заказывать могут вошедшие покупатели с подтверждённым номером.' },
]
const acceptanceModes: { value: OrderAcceptanceMode; title: string }[] = [
  { value: 'Manual', title: 'Вручную — заказ приходит со статусом «Новый»' },
  { value: 'Auto', title: 'Автоматически — сразу «Принят»' },
]

function RulesSection({ shop }: { shop: ShopManageDto }) {
  const refresh = useRefreshShop(shop.id)
  const s = shop.settings
  const [customerMode, setCustomerMode] = useState<ShopCustomerMode>(s.customerMode)
  const [acceptanceMode, setAcceptanceMode] = useState<OrderAcceptanceMode>(s.acceptanceMode)
  const [allowCustomerCancel, setAllowCustomerCancel] = useState(s.allowCustomerCancel)
  const [trackStock, setTrackStock] = useState(s.trackStock)
  const [saved, setSaved] = useState(false)
  const strictSelectable = shop.phoneVerificationAvailable || s.customerMode === 'VerifiedPhoneOnly'

  const save = useMutation({
    mutationFn: () => shopsApi.updateSettings(shop.id, { customerMode, acceptanceMode, allowCustomerCancel, trackStock }),
    onMutate: () => setSaved(false),
    onSuccess: (updated) => {
      refresh.set(updated)
      setSaved(true)
      setTimeout(() => setSaved(false), 2500)
    },
  })

  return (
    <Card className="p-6">
      <h2 className="text-[15px] font-semibold text-ink mb-1">Правила приёма заказов</h2>
      <p className="text-sm text-ink-soft mb-5">Изменения действуют только на новые заказы — созданные ранее живут по прежним правилам.</p>

      <fieldset className="mb-5">
        <legend className="text-[13px] font-medium text-[#4A4038] mb-2">Кто может заказать</legend>
        <div className="flex flex-col gap-2">
          {customerModes.map((m) => {
            const disabled = m.value === 'VerifiedPhoneOnly' && !strictSelectable
            return (
              <label key={m.value} className={`flex items-start gap-3 rounded-xl border p-3.5 ${customerMode === m.value ? 'border-gold bg-cream-deep/50' : 'border-line'} ${disabled ? 'opacity-60' : 'cursor-pointer'}`}>
                <input
                  type="radio"
                  name="customerMode"
                  className="mt-1 accent-gold"
                  checked={customerMode === m.value}
                  disabled={disabled}
                  onChange={() => setCustomerMode(m.value)}
                />
                <span>
                  <span className="block text-sm font-medium text-ink">{m.title}</span>
                  <span className="block text-xs text-ink-soft mt-0.5">{m.text}</span>
                  {disabled && <span className="block text-xs text-danger mt-1">Подтверждение телефона сейчас недоступно на платформе — режим включить нельзя.</span>}
                </span>
              </label>
            )
          })}
        </div>
        {customerMode === 'VerifiedPhoneOnly' && (
          <p role="note" className="mt-3 text-sm text-warning bg-warning-bg rounded-xl px-4 py-3">
            Подтвердить номер можно только через MAX — покупатели без MAX не смогут заказать.
          </p>
        )}
      </fieldset>

      <fieldset className="mb-5">
        <legend className="text-[13px] font-medium text-[#4A4038] mb-2">Приём заказа</legend>
        <div className="flex flex-col gap-2">
          {acceptanceModes.map((m) => (
            <label key={m.value} className="flex items-center gap-3 text-sm text-ink cursor-pointer">
              <input type="radio" name="acceptanceMode" className="accent-gold" checked={acceptanceMode === m.value} onChange={() => setAcceptanceMode(m.value)} />
              {m.title}
            </label>
          ))}
        </div>
      </fieldset>

      <div className="flex flex-col gap-3 mb-5">
        <label className="flex items-center gap-3 text-sm text-ink cursor-pointer">
          <input type="checkbox" className="w-4 h-4 accent-gold" checked={allowCustomerCancel} onChange={(e) => setAllowCustomerCancel(e.target.checked)} />
          Покупатель может отменить заказ сам
        </label>
        <label className="flex items-start gap-3 text-sm text-ink cursor-pointer">
          <input type="checkbox" className="w-4 h-4 mt-0.5 accent-gold" checked={trackStock} onChange={(e) => setTrackStock(e.target.checked)} />
          <span>
            Учитывать остатки
            <span className="block text-xs text-ink-soft mt-0.5">
              Выключение не стирает остатки товаров; включение начинает с текущих цифр. Товар с пустым остатком продаётся без ограничения.
            </span>
          </span>
        </label>
      </div>

      {save.isError && <div className="mb-3"><InlineError>{getCatalogErrorMessage(save.error, 'Не удалось сохранить настройки.')}</InlineError></div>}
      <div className="flex items-center gap-3">
        <Button loading={save.isPending} onClick={() => save.mutate()}>
          Сохранить правила
        </Button>
        <SavedNote show={saved} />
      </div>
    </Card>
  )
}

const legalForms: { value: NonNullable<SellerInfoInput['legalForm']>; label: string }[] = [
  { value: 'Ip', label: 'Индивидуальный предприниматель' },
  { value: 'Company', label: 'Организация (ООО и др.)' },
  { value: 'SelfEmployed', label: 'Самозанятый' },
]

/** [legal L2] — all fields optional in cycle 1 (`requiredFields` is empty); shown to buyers on the storefront when filled. */
function SellerSection({ shop }: { shop: ShopManageDto }) {
  const refresh = useRefreshShop(shop.id)
  const sel = shop.seller
  const [legalForm, setLegalForm] = useState<string>(sel.legalForm ?? '')
  const [legalName, setLegalName] = useState(sel.legalName ?? '')
  const [inn, setInn] = useState(sel.inn ?? '')
  const [ogrn, setOgrn] = useState(sel.ogrn ?? '')
  const [legalAddress, setLegalAddress] = useState(sel.legalAddress ?? '')
  const [saved, setSaved] = useState(false)

  const save = useMutation({
    mutationFn: () =>
      shopsApi.updateSeller(shop.id, {
        legalForm: (legalForm || null) as SellerInfoInput['legalForm'],
        legalName: legalName.trim(),
        inn: inn.trim(),
        ogrn: ogrn.trim(),
        legalAddress: legalAddress.trim(),
      }),
    onMutate: () => setSaved(false),
    onSuccess: (updated) => {
      refresh.set(updated)
      setSaved(true)
      setTimeout(() => setSaved(false), 2500)
    },
  })

  return (
    <Card className="p-6">
      <h2 className="text-[15px] font-semibold text-ink mb-1">Реквизиты продавца</h2>
      <p className="text-sm text-ink-soft mb-5">Необязательно. Заполненные реквизиты показываются покупателям на странице магазина.</p>
      <div className="flex flex-col gap-4">
        <div className="flex flex-col gap-1.5">
          <label htmlFor="seller-form" className="text-[13px] font-medium text-[#4A4038]">
            Правовая форма
          </label>
          <select
            id="seller-form"
            value={legalForm}
            onChange={(e) => setLegalForm(e.target.value)}
            className="rounded-xl border border-line px-4 py-3 text-sm bg-white text-ink outline-none focus:border-gold"
          >
            <option value="">Не указана</option>
            {legalForms.map((f) => (
              <option key={f.value} value={f.value}>
                {f.label}
              </option>
            ))}
          </select>
        </div>
        <Input label="Наименование" value={legalName} onChange={(e) => setLegalName(e.target.value)} placeholder="ИП Иванов Иван Иванович" />
        <div className="grid sm:grid-cols-2 gap-4">
          <Input label="ИНН" inputMode="numeric" value={inn} onChange={(e) => setInn(e.target.value.replace(/\D/g, ''))} maxLength={12} />
          <Input label="ОГРН / ОГРНИП" inputMode="numeric" value={ogrn} onChange={(e) => setOgrn(e.target.value.replace(/\D/g, ''))} maxLength={15} />
        </div>
        <Input label="Юридический адрес" value={legalAddress} onChange={(e) => setLegalAddress(e.target.value)} />
        {save.isError && <InlineError>{getGoodsErrorMessage(save.error, 'Не удалось сохранить реквизиты.')}</InlineError>}
        <div className="flex items-center gap-3">
          <Button loading={save.isPending} onClick={() => save.mutate()}>
            Сохранить реквизиты
          </Button>
          <SavedNote show={saved} />
        </div>
      </div>
    </Card>
  )
}
