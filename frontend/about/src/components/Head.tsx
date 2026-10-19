import type { PageCopy } from '../content/types'

interface HeadProps {
  copy: PageCopy
  canonical: string
}

/** Содержимое <head> главной (API_CONTRACT_CYCLE44 §44.25.1). React экранирует атрибуты и текст. */
export function Head({ copy, canonical }: HeadProps) {
  return (
    <>
      <title>{copy.meta.title}</title>
      <meta name="description" content={copy.meta.description} />
      <link rel="canonical" href={canonical} />
      <meta property="og:type" content="website" />
      <meta property="og:title" content={copy.meta.title} />
      <meta property="og:description" content={copy.meta.description} />
      <meta property="og:url" content={canonical} />
      <meta property="og:locale" content="ru_RU" />
    </>
  )
}
