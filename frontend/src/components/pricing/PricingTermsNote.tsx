import { StorefrontBlocks } from '@/components/storefront/StorefrontBlocks'
import { STOREFRONT_PAGES } from '@/utils/storefrontMarkup'

/**
 * Подпись под сеткой тарифов `/pricing` «Записи» и «Заказов» (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.8.2, ТЛ48-02, п. 6.13.4):
 * формула суммы, ссылки на условия и налоговая оговорка. Статично, из contracts/cycle48/storefront-pages.json; оговорка
 * не сворачивается и не прячется за кликом (п. 6.13.6).
 */
export function PricingTermsNote({ site }: { site: 'zapis' | 'goods' }) {
  return (
    <div className="max-w-[720px] text-sm text-ink-soft space-y-3 mb-16" data-testid="pricing-terms-note">
      <StorefrontBlocks blocks={STOREFRONT_PAGES.pricing.termsUnderGrid.blocks} site={site} linkMode="router" />
    </div>
  )
}
