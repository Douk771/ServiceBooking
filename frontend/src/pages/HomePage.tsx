import { useQuery } from '@tanstack/react-query'
import { ServiceLanding } from '../components/landing/ServiceLanding'
import { ZapisCompanyCatalog } from './home/ZapisCompanyCatalog'
import { companiesPageQuery } from './home/companiesPageQuery'
import { zapisLanding } from './home/zapisLanding'

/**
 * `/` «Записи» — единый шаблон главной с каталогом салонов (ARCHITECTURE_CYCLE38.md §38.4.2).
 * Компании запрашиваются сразу: пока ответа нет или салонов нет совсем, каталога и строки «Ищете, куда записаться?» нет.
 * Ошибка запроса — каталог на месте, со своим сообщением об ошибке.
 */
export function HomePage() {
  const { data, isError } = useQuery(companiesPageQuery('', 1))
  const showCatalog = isError || (data !== undefined && data.total > 0)
  return <ServiceLanding config={zapisLanding} catalog={showCatalog ? <ZapisCompanyCatalog /> : null} />
}
