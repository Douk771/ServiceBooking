/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Yandex SmartCaptcha client site key (guest-booking bot protection). Optional in dev. */
  readonly VITE_SMARTCAPTCHA_SITEKEY?: string
  /** 'on' shows the "Все продукты EZBOOK" footer link in release builds (ARCHITECTURE_CYCLE44 §44.8). */
  readonly VITE_PRODUCTS_PAGE_LINK?: string
  /** Overrides of the public site addresses (src/utils/publicSites.ts). */
  readonly VITE_SITE_URL_ZAPIS?: string
  readonly VITE_SITE_URL_GOODS?: string
  readonly VITE_SITE_URL_DOM?: string
  readonly VITE_SITE_URL_BANI?: string
  readonly VITE_SITE_URL_ABOUT?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
