import { useState } from 'react'
import { companyInitial } from '../../utils/companyInitial'

/** ARCHITECTURE_CYCLE29.md §29.8.2 — logo or letter placeholder (also when the image fails to load). */
export interface CompanyLogoMarkProps {
  name: string
  logoUrl?: string | null
  /** 'card' — 64x64 CompanyCard header; 'catalog' — 56x56 catalog card (both sites). */
  size: 'card' | 'catalog'
  className?: string
}

export function CompanyLogoMark({ name, logoUrl, size, className = '' }: CompanyLogoMarkProps) {
  const [failedSrc, setFailedSrc] = useState<string | null>(null)
  const card = size === 'card'
  const box = card ? 'w-16 h-16 rounded-[18px] border-4 border-white shrink-0' : 'w-14 h-14 rounded-2xl shrink-0'
  const px = card ? 64 : 56
  const cls = `${box}${className ? ` ${className}` : ''}`

  if (logoUrl && logoUrl !== failedSrc) {
    return (
      <img
        src={logoUrl}
        alt=""
        width={px}
        height={px}
        {...(card ? {} : { loading: 'lazy' as const, decoding: 'async' as const })}
        onError={() => setFailedSrc(logoUrl)}
        className={`${cls} object-cover`}
        data-testid="company-logo-img"
      />
    )
  }
  return (
    <div
      aria-hidden="true"
      data-testid="company-logo-initial"
      className={`${cls} bg-cream-deep text-gold-dark font-serif ${card ? 'text-2xl' : 'text-xl'} flex items-center justify-center`}
    >
      {companyInitial(name)}
    </div>
  )
}
