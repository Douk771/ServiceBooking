import { describe, it, expect } from 'vitest'
import {
  DEV_SITE_URLS,
  PRODUCTION_SITE_URLS,
  SiteUrlError,
  displayHost,
  productsPageHref,
  resolveSiteUrls,
  siteUrlEnvName,
} from './publicSites'

describe('publicSites', () => {
  it('env names', () => {
    expect(siteUrlEnvName('zapis')).toBe('VITE_SITE_URL_ZAPIS')
    expect(siteUrlEnvName('about')).toBe('VITE_SITE_URL_ABOUT')
  })

  it('defaults per mode', () => {
    expect(resolveSiteUrls({}, 'production')).toEqual(PRODUCTION_SITE_URLS)
    expect(resolveSiteUrls({}, 'development')).toEqual(DEV_SITE_URLS)
  })

  it('override is trimmed; empty string falls back to default', () => {
    const urls = resolveSiteUrls({ VITE_SITE_URL_GOODS: ' https://g.example.org ', VITE_SITE_URL_DOM: '  ' }, 'production')
    expect(urls.goods).toBe('https://g.example.org')
    expect(urls.dom).toBe(PRODUCTION_SITE_URLS.dom)
  })

  it.each([
    'not a url',
    'ftp://x.example.org',
    'https://x.example.org/',
    'https://x.example.org/path',
    'https://x.example.org?a=1',
    'https://x.example.org#h',
    'https://user:pw@x.example.org',
    'http://x.example.org',
  ])('rejects %s in production', (bad) => {
    expect(() => resolveSiteUrls({ VITE_SITE_URL_ABOUT: bad }, 'production')).toThrow(SiteUrlError)
  })

  it('allows http for localhost in production and http in development', () => {
    expect(resolveSiteUrls({ VITE_SITE_URL_ABOUT: 'http://localhost:5177' }, 'production').about).toBe('http://localhost:5177')
    expect(resolveSiteUrls({ VITE_SITE_URL_ABOUT: 'http://x.example.org' }, 'development').about).toBe('http://x.example.org')
  })

  it('displayHost keeps the port', () => {
    expect(displayHost('https://goods.ezbook.ru')).toBe('goods.ezbook.ru')
    expect(displayHost('http://localhost:5177')).toBe('localhost:5177')
  })

  describe('productsPageHref', () => {
    it('dev: always', () => expect(productsPageHref({ DEV: true })).toBe('http://localhost:5177/'))
    it('prod without flag: null', () => expect(productsPageHref({ DEV: false })).toBeNull())
    it('prod with flag: production address', () =>
      expect(productsPageHref({ DEV: false, VITE_PRODUCTS_PAGE_LINK: 'on' })).toBe('https://about.ezbook.ru/'))
    it('only strict "on" enables it', () => expect(productsPageHref({ VITE_PRODUCTS_PAGE_LINK: 'true' })).toBeNull())
    it('bad override: null, never throws', () =>
      expect(productsPageHref({ VITE_PRODUCTS_PAGE_LINK: 'on', VITE_SITE_URL_ABOUT: 'nope' })).toBeNull())
  })
})
