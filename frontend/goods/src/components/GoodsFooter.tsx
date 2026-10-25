import { Link } from 'react-router-dom'
import { ProductsPageLink } from '@/components/sites/ProductsPageLink'
import { OperatorRequisitesLine } from '@/components/sites/OperatorRequisites'

/**
 * Legal links stay on this domain (SPEC US-23-06, Р7): real <Link>s to the goods routes of the shared
 * legal pages, not to ezbook.ru.
 */
export function GoodsFooter() {
  return (
    <footer className="border-t border-line mt-16">
      <div className="max-w-[1180px] mx-auto px-4 sm:px-8 py-6 flex flex-col items-center gap-3 text-xs text-ink-soft">
        <OperatorRequisitesLine linkMode="router" site="goods" />
        <div className="flex flex-wrap items-center justify-center gap-x-6 gap-y-2">
          <span>© {new Date().getFullYear()} ezbook · Заказы</span>
          <Link to="/privacy" className="hover:text-ink transition-colors">
            Политика обработки персональных данных
          </Link>
          <Link to="/terms" className="hover:text-ink transition-colors">
            Пользовательское соглашение
          </Link>
          <Link to="/terms-owner" className="hover:text-ink transition-colors">
            Соглашение с компанией
          </Link>
          <Link to="/pdn-consent" className="hover:text-ink transition-colors">
            Согласие на обработку ПДн
          </Link>
          <Link to="/data-request" className="hover:text-ink transition-colors">
            Обращение по своим данным
          </Link>
          <ProductsPageLink />
        </div>
      </div>
    </footer>
  )
}
