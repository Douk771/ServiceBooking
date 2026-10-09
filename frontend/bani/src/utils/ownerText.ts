// OwnerTextChecks.Check (bani-vectors.json `ownerText`): resource description and item names. Errors block saving, warnings do not.
export type OwnerTextCode =
  | 'ForbiddenWords' | 'CancellationTermsInText' | 'MandatoryExtraCharge' | 'HealthClaim' | 'Passport' | 'CardNumber' | 'PassportNumber'

export const OWNER_TEXT_ERROR = 'Не используйте слова «задаток», «невозвратный», «депозит»'

export const OWNER_TEXT_WARNINGS: Record<Exclude<OwnerTextCode, 'ForbiddenWords'>, string> = {
  CancellationTermsInText: 'Условия отмены задаёт выбранный шаблон — другие условия в описании не действуют',
  MandatoryExtraCharge: 'Все обязательные платежи должны быть в цене часов — не требуйте доплат на месте',
  HealthClaim: 'Не обещайте лечебного или оздоровительного эффекта',
  Passport: 'Не просите гостя прислать фото паспорта',
  CardNumber: 'Похоже на номер карты — не публикуйте данные карт',
  PassportNumber: 'Похоже на паспортные данные — не публикуйте их',
}

const norm = (s: string) => s.toLowerCase().replace(/ё/g, 'е')
const CARD = /(?<!\d)(?:\d{4}[ -]?){3}\d{4}(?!\d)/
const PASSPORT_NUMBER = /паспорт\D{0,20}\d{4}\s?\d{6}/

export function checkOwnerText(text: string): { errors: OwnerTextCode[]; warnings: OwnerTextCode[] } {
  const t = norm(text)
  const errors: OwnerTextCode[] = /задат|невозвратн|депозит/.test(t) ? ['ForbiddenWords'] : []
  const warnings: OwnerTextCode[] = []
  if (/штраф|неустойк|неявк|не\s+возвращ|невозврат(?!н)|условия\s+отмены|отмен\w*\s+(?:за|менее|позже)/.test(t)) warnings.push('CancellationTermsInText')
  if (/(?<!\p{L})доплат/u.test(t)) warnings.push('MandatoryExtraCharge')
  if (/лечебн|оздоровит|детокс|исцел|иммунитет|лечени|лечит|целебн|противопоказани|полезно\s+при/.test(t)) warnings.push('HealthClaim')
  if (PASSPORT_NUMBER.test(t)) warnings.push('PassportNumber')
  else if (/паспорт/.test(t)) warnings.push('Passport')
  if (CARD.test(text)) warnings.push('CardNumber')
  return { errors, warnings }
}
