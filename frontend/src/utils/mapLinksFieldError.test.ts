import { describe, it, expect } from 'vitest'
import { mapLinksFieldError } from './mapLinksFieldError'

describe('mapLinksFieldError', () => {
  it('routes the 2ГИС-specific message to twoGisUrl', () => {
    expect(mapLinksFieldError('Ждём ссылку на 2ГИС — например, https://2gis.ru/barnaul/firm/...')).toBe('twoGisUrl')
  })

  it('routes the Яндекс-specific message to yandexMapsUrl', () => {
    expect(mapLinksFieldError('Ждём ссылку на Яндекс Карты — например, https://yandex.ru/maps/org/.../...')).toBe(
      'yandexMapsUrl',
    )
  })

  it('routes the reschedule-window message to clientRescheduleMinHours', () => {
    expect(mapLinksFieldError('Окно переноса — от 0 до 168 часов')).toBe('clientRescheduleMinHours')
  })

  it('routes an ambiguous link message (scheme/length) to yandexMapsUrl by default', () => {
    expect(mapLinksFieldError('Ссылка должна начинаться с https://')).toBe('yandexMapsUrl')
    expect(mapLinksFieldError('Ссылка слишком длинная — не больше 500 символов')).toBe('yandexMapsUrl')
  })

  it('returns null for unrelated or missing text', () => {
    expect(mapLinksFieldError('Slug already taken')).toBeNull()
    expect(mapLinksFieldError(undefined)).toBeNull()
    expect(mapLinksFieldError(null)).toBeNull()
    expect(mapLinksFieldError('')).toBeNull()
  })
})
