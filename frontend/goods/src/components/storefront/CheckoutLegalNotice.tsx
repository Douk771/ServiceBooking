import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { findSection, splitLegalSections } from '@/utils/legalSections'
import { legalNoticeApi } from '../../api/legalNotice'

/**
 * The line under «Заказать» [legal L4]: the text from `OrderCheckoutNotice` when the lawyer's text exists,
 * otherwise a neutral sentence with the two links (never a "ТРЕБУЕТСЯ ТЕКСТ…" placeholder — CI greps for it).
 */
export function CheckoutLegalNotice() {
  const { data, isLoading } = useQuery({
    queryKey: ['legal-text', 'OrderCheckoutNotice'],
    queryFn: legalNoticeApi.orderCheckout,
    staleTime: 5 * 60 * 1000,
    retry: false, // 404 = "not written yet"; do not hammer it
  })

  if (isLoading) return <div className="h-4 w-3/4 mx-auto rounded bg-cream-deep animate-pulse" aria-hidden="true" />

  if (data?.contentHtml) {
    const sections = splitLegalSections(data.contentHtml)
    const html = (findSection(sections, 'Короткая строка') ?? findSection(sections, 'Текст для покупателя'))?.html ?? data.contentHtml
    return <div className="legal-content text-xs text-muted text-center [&_a]:text-gold [&_p]:mb-0" dangerouslySetInnerHTML={{ __html: html }} />
  }

  return (
    <p className="text-xs text-muted text-center">
      Оформляя заказ, вы соглашаетесь с{' '}
      <Link to="/terms" target="_blank" className="text-gold hover:text-gold-dark">
        пользовательским соглашением
      </Link>{' '}
      и{' '}
      <Link to="/privacy" target="_blank" className="text-gold hover:text-gold-dark">
        политикой обработки персональных данных
      </Link>
      .
    </p>
  )
}
