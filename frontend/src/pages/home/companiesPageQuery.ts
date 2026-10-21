import { companiesApi } from '../../api/companies'

const PAGE_SIZE = 20

/** Запрос страницы каталога. Главная берёт первую страницу без поиска заранее (пуст ли каталог), каталог — тот же кэш. */
export function companiesPageQuery(search: string, page: number) {
  return {
    queryKey: ['companies-public', search, page] as const,
    queryFn: () => companiesApi.getPublic({ search: search || undefined, page, pageSize: PAGE_SIZE }),
    // Каталог монтируется, когда главная уже получила ошибку: повтор при монтировании сбросил бы её в «загрузку»,
    // главная убрала бы каталог, и так по кругу. Ошибка остаётся ошибкой до перезагрузки страницы.
    retryOnMount: false,
  }
}
