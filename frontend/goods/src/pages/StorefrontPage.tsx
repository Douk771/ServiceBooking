import { useEffect, useMemo, useState } from 'react'
import { useParams, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { CompanyMapLinks } from '@/components/company/CompanyMapLinks'
import { Icon } from '@/components/ui/Icon'
import { formatPhone, telHref } from '@/utils/phone'
import { storefrontApi } from '../api/storefront'
import { CartPanel } from '../components/storefront/CartPanel'
import { ProductCard } from '../components/storefront/ProductCard'
import { ErrorState, LoadingList, Skeleton } from '../components/StatePanels'
import { useCart } from '../hooks/useCart'
import { quantityRule } from '../utils/cart'
import { orderTotal } from '../utils/orderMoney'
import { formatMoney } from '../utils/quantityFormat'
import { getGoodsErrorMessage, httpStatus } from '../utils/orderError'
import { NotFoundPage } from './NotFoundPage'
import type { StorefrontProductDto } from '../types'

const LEGAL_FORM_LABELS: Record<string, string> = { Ip: 'ИП', Company: 'Организация', SelfEmployed: 'Самозанятый' }

/** US-23-18 — the shop page at `/:slug`: header, categories with products, cart bar and slide-over cart. */
export function StorefrontPage() {
  const { slug = '' } = useParams<{ slug: string }>()
  const [params, setParams] = useSearchParams()
  const cart = useCart(slug)
  const [cartOpen, setCartOpen] = useState(false)

  const shopQuery = useQuery({
    queryKey: ['storefront', slug],
    queryFn: () => storefrontApi.get(slug),
    retry: (count, err) => httpStatus(err) === undefined && count < 2,
  })
  const shop = shopQuery.data

  const products = useMemo(() => {
    const m = new Map<string, StorefrontProductDto>()
    shop?.categories.forEach((c) => c.products.forEach((p) => m.set(p.id, p)))
    return m
  }, [shop])

  // Back from /login in the strict mode: `?checkout=1` opens the cart with the saved items (§399.3).
  const checkoutFlag = params.get('checkout')
  useEffect(() => {
    if (checkoutFlag && shop) {
      setCartOpen(true)
      const next = new URLSearchParams(params)
      next.delete('checkout')
      setParams(next, { replace: true })
    }
  }, [checkoutFlag, shop, params, setParams])

  useEffect(() => {
    if (shop) document.title = `${shop.name} — заказ онлайн`
    return () => {
      document.title = 'ezbook · Заказы'
    }
  }, [shop])

  if (shopQuery.isLoading)
    return (
      <main className="max-w-[860px] mx-auto px-4 sm:px-8 pt-10">
        <Skeleton className="h-28 mb-8" />
        <LoadingList rows={3} rowClass="h-32" />
      </main>
    )
  if (shopQuery.isError || !shop) {
    if (httpStatus(shopQuery.error) === 404) return <NotFoundPage title="Магазин не найден" hint="Проверьте ссылку или QR-код — адрес мог измениться." />
    return (
      <main className="max-w-[860px] mx-auto px-4 pt-10">
        <ErrorState message={getGoodsErrorMessage(shopQuery.error, 'Не удалось загрузить магазин.')} onRetry={() => void shopQuery.refetch()} />
      </main>
    )
  }

  if (!shop.isAvailable)
    return (
      <main className="max-w-[560px] mx-auto px-4 py-24 text-center">
        <Icon name="store" size={36} strokeWidth={1.3} className="mx-auto text-muted mb-4" />
        <h1 className="font-serif text-2xl text-ink">Магазин недоступен</h1>
        <p className="mt-2 text-sm text-ink-soft">{shop.notAcceptingReason ?? 'Сейчас страница этого магазина закрыта.'}</p>
      </main>
    )

  const cartCount = cart.items.length
  const preview = orderTotal(cart.items.map((i) => ({ unit: products.get(i.productId)?.unit ?? 'Piece', price: i.unitPriceSeen, quantity: i.quantity })))

  const setQuantity = (p: StorefrontProductDto, quantity: number | null) => {
    if (quantity === null) cart.remove(p.id)
    else cart.setLine({ productId: p.id, quantity: Math.max(quantityRule(p).min, quantity), unitPriceSeen: cart.byId.get(p.id)?.unitPriceSeen ?? p.price })
  }

  const seller = shop.seller
  const sellerBits = seller ? [seller.legalForm ? (LEGAL_FORM_LABELS[seller.legalForm] ?? seller.legalForm) : null, seller.legalName, seller.inn ? `ИНН ${seller.inn}` : null, seller.ogrn ? `ОГРН ${seller.ogrn}` : null, seller.legalAddress].filter(Boolean) : []

  return (
    <main className={`max-w-[860px] mx-auto px-4 sm:px-8 pt-8 ${cartCount > 0 ? 'pb-28' : 'pb-8'}`}>
      <header className="flex gap-5 items-start mb-6">
        {shop.logoUrl ? (
          <img src={shop.logoUrl} alt="" className="w-20 h-20 sm:w-24 sm:h-24 rounded-2xl object-cover shrink-0" />
        ) : (
          <span className="w-20 h-20 sm:w-24 sm:h-24 rounded-2xl bg-cream-deep flex items-center justify-center text-gold-dark font-serif text-3xl shrink-0">{shop.name[0]}</span>
        )}
        <div className="min-w-0">
          <h1 className="font-serif text-[30px] sm:text-[38px] leading-tight text-ink">{shop.name}</h1>
          {shop.description && <p className="mt-1.5 text-[15px] text-ink-soft max-w-[560px]">{shop.description}</p>}
          <div className="mt-2.5 flex flex-col gap-1 text-sm text-ink-soft">
            {(shop.address || shop.cityName) && (
              <p className="flex items-start gap-1.5">
                <Icon name="map-pin" size={14} strokeWidth={1.8} className="mt-0.5 shrink-0" />
                <span>{[shop.cityName, shop.address].filter(Boolean).join(', ')}</span>
              </p>
            )}
            {shop.phone && (
              <p className="flex items-center gap-1.5">
                <Icon name="phone" size={14} strokeWidth={1.8} />
                <a href={telHref(shop.phone) || undefined} className="text-ink hover:text-gold-dark font-medium">
                  {formatPhone(shop.phone)}
                </a>
              </p>
            )}
            <CompanyMapLinks yandexUrl={shop.yandexMapsUrl} twoGisUrl={shop.twoGisUrl} />
          </div>
        </div>
      </header>

      {!shop.acceptingOrders && (
        <div role="status" className="mb-6 rounded-xl bg-warning-bg text-warning text-sm font-medium px-4 py-3">
          {shop.notAcceptingReason ?? 'Сейчас магазин не принимает заказы.'}
        </div>
      )}

      {shop.categories.length === 0 ? (
        <div className="rounded-2xl border border-dashed border-line-strong bg-white/60 px-6 py-14 text-center">
          <p className="text-ink font-medium">В каталоге пока нет товаров</p>
          <p className="text-sm text-ink-soft mt-1">Загляните позже.</p>
        </div>
      ) : (
        <>
          {shop.categories.length > 1 && (
            <nav aria-label="Категории" className="sticky top-[68px] z-20 -mx-4 sm:mx-0 px-4 sm:px-0 py-2.5 mb-4 bg-cream/95 backdrop-blur flex gap-2 overflow-x-auto">
              {shop.categories.map((c) => (
                <a key={c.id ?? 'other'} href={`#cat-${c.id ?? 'other'}`} className="whitespace-nowrap rounded-full border border-line bg-white px-4 py-1.5 text-sm font-medium !text-ink-soft hover:!text-ink hover:border-line-strong">
                  {c.name}
                </a>
              ))}
            </nav>
          )}
          <div className="flex flex-col gap-9">
            {shop.categories.map((c) => (
              <section key={c.id ?? 'other'} id={`cat-${c.id ?? 'other'}`} aria-label={c.name} className="scroll-mt-32">
                <h2 className="font-serif text-2xl text-ink mb-3">{c.name}</h2>
                <ul className="flex flex-col gap-3">
                  {c.products.map((p) => (
                    <ProductCard key={p.id} product={p} quantity={cart.byId.get(p.id)?.quantity} onChange={(q) => setQuantity(p, q)} />
                  ))}
                </ul>
              </section>
            ))}
          </div>
        </>
      )}

      {sellerBits.length > 0 && (
        <section aria-label="Продавец" className="mt-12 pt-5 border-t border-line text-xs text-muted">
          <p className="font-semibold text-ink-soft mb-1">Продавец</p>
          <p>{sellerBits.join(', ')}</p>
        </section>
      )}

      {cartCount > 0 && !cartOpen && (
        <div className="fixed bottom-0 inset-x-0 z-30 p-3 sm:p-4 pointer-events-none">
          <button
            type="button"
            onClick={() => setCartOpen(true)}
            className="pointer-events-auto mx-auto max-w-[860px] w-full flex items-center justify-between gap-4 rounded-full bg-ink text-cream px-6 py-4 shadow-modal hover:bg-ink/95 transition-colors"
          >
            <span className="flex items-center gap-2.5 font-semibold">
              <Icon name="shopping-bag" size={18} />
              Корзина · {cartCount} {pluralPositions(cartCount)}
            </span>
            <span className="font-serif text-lg" data-testid="cart-bar-total">
              {formatMoney(preview.total, preview.isApproximate)}
            </span>
          </button>
        </div>
      )}

      {cartOpen && <CartPanel slug={slug} shop={shop} products={products} cart={cart} onClose={() => setCartOpen(false)} />}
    </main>
  )
}

function pluralPositions(n: number): string {
  const m10 = n % 10
  const m100 = n % 100
  if (m10 === 1 && m100 !== 11) return 'позиция'
  if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return 'позиции'
  return 'позиций'
}
