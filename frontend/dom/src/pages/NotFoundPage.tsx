import { Link } from 'react-router-dom'

/** Unknown path, or a company/house/booking address that does not exist (404 carries no hint either way). */
export function NotFoundPage({ title = 'Страница не найдена', hint }: { title?: string; hint?: string }) {
  return (
    <main className="max-w-[560px] mx-auto px-4 py-24 text-center">
      <p className="font-serif text-[64px] leading-none text-line-strong">404</p>
      <h1 className="mt-4 font-serif text-2xl text-ink">{title}</h1>
      <p className="mt-2 text-sm text-ink-soft">{hint ?? 'Проверьте ссылку — возможно, она набрана с ошибкой или устарела.'}</p>
      <Link to="/" className="inline-block mt-6 text-sm font-semibold text-gold hover:text-gold-dark">
        К каталогу
      </Link>
    </main>
  )
}
