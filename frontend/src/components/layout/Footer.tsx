import { Link } from 'react-router-dom'
import { ProductsPageLink } from '@/components/sites/ProductsPageLink'
import { OperatorRequisitesLine } from '@/components/sites/OperatorRequisites'

/**
 * Site-wide footer. Legal links required by US-36 п. 4 and, from cycle 5, API_CONTRACT_CYCLE5.md §43
 * (the four new document routes) and §48.4 (`/data-request`) — real `<a>`/`<Link>` elements, not
 * onClick handlers, so crawlers and users without JS both see them (ARCHITECTURE.md §4.5).
 */
export function Footer() {
  return (
    // print:hidden — see the matching comment in Navbar.tsx.
    <footer className="border-t border-line mt-16 print:hidden">
      <div className="max-w-[1180px] mx-auto px-4 sm:px-8 py-6 flex flex-col items-center gap-3 text-xs text-ink-soft">
        <OperatorRequisitesLine linkMode="router" site="zapis" />
        <div className="flex flex-wrap items-center justify-center gap-x-6 gap-y-2">
          <span>© {new Date().getFullYear()} EZBOOK</span>
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
          <Link to="/channel-risk" className="hover:text-ink transition-colors">
            Риски канала уведомлений
          </Link>
          <Link to="/offer-channel" className="hover:text-ink transition-colors">
            Оферта на подключение канала
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
