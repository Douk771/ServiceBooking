/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Yandex SmartCaptcha client site key (guest-booking bot protection). Optional in dev. */
  readonly VITE_SMARTCAPTCHA_SITEKEY?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
