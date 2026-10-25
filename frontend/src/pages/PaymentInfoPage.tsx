import { useEffect } from 'react'
import { StorefrontBlocks } from '@/components/storefront/StorefrontBlocks'
import { STOREFRONT_PAGES, type StorefrontSite } from '@/utils/storefrontMarkup'

/**
 * «Оплата и возврат» (/payment-info) on all four sites — ARCHITECTURE_CYCLE48_STOREFRONT.md §48.7.1, US-48-03.
 * Static: the text is contracts/cycle48/storefront-pages.json, the values are contracts/legal/operator-public.json.
 * No API calls, so the page is shown even when the API is down and for a user with a pending consent gate (bypass path).
 */
export function PaymentInfoPage({ site }: { site: StorefrontSite }) {
  const page = STOREFRONT_PAGES.pages.paymentInfo
  useEffect(() => {
    document.title = page.title
  }, [page.title])
  return (
    <main className="max-w-[760px] mx-auto px-4 sm:px-8 pt-11 pb-24">
      <h1 className="font-serif text-[30px] md:text-[36px] font-medium text-ink mb-6">{page.h1}</h1>
      <StorefrontBlocks blocks={page.blocks} site={site} linkMode="router" />
    </main>
  )
}
