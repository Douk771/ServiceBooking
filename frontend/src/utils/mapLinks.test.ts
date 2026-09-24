import { describe, it, expect } from 'vitest'
import { buildYandexMapsUrl, buildTwoGisUrl } from './mapLinks'

describe('mapLinks — ARCHITECTURE_CYCLE13.md §205/§239', () => {
  describe('buildYandexMapsUrl', () => {
    it('searches by "city, address" when no point is known', () => {
      const url = buildYandexMapsUrl({ address: 'Ленина, 5', cityName: 'Барнаул' })
      expect(url).toBe('https://yandex.ru/maps/?text=' + encodeURIComponent('Барнаул, Ленина, 5'))
    })

    it('falls back to just the address when there is no city', () => {
      const url = buildYandexMapsUrl({ address: 'Ленина, 5', cityName: null })
      expect(url).toBe('https://yandex.ru/maps/?text=' + encodeURIComponent('Ленина, 5'))
    })

    it('uses a marker centred on the point, in lon,lat order, when a point is known', () => {
      // Deliberately asymmetric numbers — a lat/lon transposition bug can't hide behind them.
      const url = buildYandexMapsUrl({
        address: 'Ленина, 5',
        cityName: 'Барнаул',
        point: { latitude: 53.34, longitude: 83.77 },
      })
      expect(url).toContain('ll=83.77%2C53.34')
      expect(url).toContain('pt=83.77%2C53.34')
      expect(url).toContain('z=17')
      expect(url).not.toContain('text=')
    })

    it('encodes quotes, №, "к1", spaces and Cyrillic safely', () => {
      const url = buildYandexMapsUrl({ address: 'пр. Ленина, д. 5, к1, №3 "Уют"', cityName: 'Барнаул' })
      expect(url).not.toMatch(/[«»"№ ]/) // nothing dangerous survives unescaped in the query string
      expect(decodeURIComponent(url.split('?text=')[1])).toBe('Барнаул, пр. Ленина, д. 5, к1, №3 "Уют"')
    })
  })

  describe('buildTwoGisUrl', () => {
    it('always searches by text, even when a point is known', () => {
      const url = buildTwoGisUrl({
        address: 'Ленина, 5',
        cityName: 'Барнаул',
        point: { latitude: 53.34, longitude: 83.77 },
      })
      expect(url).toContain('https://2gis.ru/search/' + encodeURIComponent('Барнаул, Ленина, 5'))
    })

    it('appends the point as a map-centring hint in lon,lat order', () => {
      const url = buildTwoGisUrl({
        address: 'Ленина, 5',
        cityName: 'Барнаул',
        point: { latitude: 53.34, longitude: 83.77 },
      })
      expect(url).toContain('?m=83.77%2C53.34%2F17')
    })

    it('omits the ?m= hint entirely when there is no point', () => {
      const url = buildTwoGisUrl({ address: 'Ленина, 5', cityName: 'Барнаул' })
      expect(url).not.toContain('?m=')
    })

    it('works with no city at all', () => {
      const url = buildTwoGisUrl({ address: 'Ленина, 5' })
      expect(url).toBe('https://2gis.ru/search/' + encodeURIComponent('Ленина, 5'))
    })
  })
})
