import type { SVGProps } from 'react'

export type IconName =
  | 'calendar'
  | 'clock'
  | 'map-pin'
  | 'phone'
  | 'mail'
  | 'star'
  | 'star-outline'
  | 'check'
  | 'check-circle'
  | 'chevron-left'
  | 'chevron-right'
  | 'chevron-down'
  | 'search'
  | 'user'
  | 'users'
  | 'scissors'
  | 'store'
  | 'bar-chart'
  | 'megaphone'
  | 'settings'
  | 'log-out'
  | 'menu'
  | 'plus'
  | 'arrow-right'
  | 'x'
  | 'credit-card'
  | 'alert-circle'
  | 'trash'
  | 'image'
  | 'copy'
  | 'external-link'

/**
 * Minimal line-icon set for the premium redesign (see design_handoff_site_redesign/README.md).
 * 24×24 viewBox, stroke=currentColor, replaces emoji everywhere in the app. Paths for icons that
 * appear in the design mockups are copied verbatim from the .dc.html files; the rest (mail, users,
 * settings, menu, plus, bar-chart, megaphone, credit-card, alert-circle, star-outline, chevron-down)
 * are hand-authored in the same stroke-width/cap/join style since the mockups didn't need them.
 */
const paths: Record<IconName, JSX.Element> = {
  calendar: (
    <>
      <rect x="3" y="5" width="18" height="16" rx="3" />
      <path d="M3 10h18M8 3v4M16 3v4" />
    </>
  ),
  clock: (
    <>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 7v5l3.5 2" />
    </>
  ),
  'map-pin': (
    <>
      <path d="M12 21s7-7.58 7-12a7 7 0 1 0-14 0c0 4.42 7 12 7 12z" />
      <circle cx="12" cy="9" r="2.5" />
    </>
  ),
  phone: (
    <path d="M6.5 3.5c1 0 2 .7 2.3 1.7l.7 2a2 2 0 0 1-.5 2.1l-1 .9a12 12 0 0 0 5.4 5.4l.9-1a2 2 0 0 1 2.1-.5l2 .7c1 .3 1.7 1.3 1.7 2.3v1.9c0 1.4-1.2 2.5-2.6 2.3C9.9 20.4 3.6 14.1 2.7 6.6 2.5 5.2 3.6 4 5 4h1.5z" />
  ),
  mail: (
    <>
      <rect x="3" y="5" width="18" height="14" rx="2.5" />
      <path d="M4 6.5l8 6.5 8-6.5" />
    </>
  ),
  star: <path d="M12 3.5l2.6 5.3 5.9.9-4.3 4.1 1 5.8-5.2-2.7-5.2 2.7 1-5.8-4.3-4.1 5.9-.9L12 3.5z" />,
  'star-outline': (
    <path d="M12 3.5l2.6 5.3 5.9.9-4.3 4.1 1 5.8-5.2-2.7-5.2 2.7 1-5.8-4.3-4.1 5.9-.9L12 3.5z" fill="none" />
  ),
  check: <path d="M5 13l4 4L19 7" strokeWidth={2.2} />,
  'check-circle': (
    <>
      <circle cx="12" cy="12" r="9" />
      <path d="M8 12.5l2.5 2.5L16 9" />
    </>
  ),
  'chevron-left': <path d="M15 6l-6 6 6 6" />,
  'chevron-right': <path d="M9 6l6 6-6 6" />,
  'chevron-down': <path d="M6 9l6 6 6-6" />,
  search: (
    <>
      <circle cx="11" cy="11" r="7" />
      <path d="M21 21l-4.3-4.3" />
    </>
  ),
  user: (
    <>
      <circle cx="12" cy="8" r="4" />
      <path d="M4 20c1.5-4 5-6 8-6s6.5 2 8 6" />
    </>
  ),
  users: (
    <>
      <circle cx="9" cy="8" r="3.5" />
      <path d="M2.5 20c1.2-3.4 4-5 6.5-5s5.3 1.6 6.5 5" />
      <path d="M16 4.2c1.5.4 2.5 1.7 2.5 3.3s-1 2.9-2.5 3.3" />
      <path d="M19 15.3c1.9.6 3.2 2.1 4 4.7" />
    </>
  ),
  scissors: (
    <>
      <circle cx="6" cy="6" r="2.2" />
      <circle cx="6" cy="18" r="2.2" />
      <path d="M8 7.5L20 19M8 16.5L20 5" />
    </>
  ),
  store: (
    <>
      <rect x="3" y="7.5" width="18" height="12" rx="2.5" />
      <path d="M8 7.5v-2a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
    </>
  ),
  'bar-chart': (
    <>
      <path d="M4 20V10M11 20V4M18 20v-7" />
    </>
  ),
  megaphone: (
    <>
      <path d="M3 10v4a1 1 0 0 0 1 1h2l1 5h2l-1-5h2l9 4V6l-9 4H4a1 1 0 0 0-1 1z" />
    </>
  ),
  settings: (
    <>
      <circle cx="12" cy="12" r="3" />
      <path d="M19 12a7 7 0 0 0-.14-1.4l1.9-1.5-2-3.4-2.3.6a7 7 0 0 0-2.4-1.4L13.6 3h-3.2l-.46 2.9a7 7 0 0 0-2.4 1.4l-2.3-.6-2 3.4 1.9 1.5A7 7 0 0 0 5 12c0 .48.05.94.14 1.4l-1.9 1.5 2 3.4 2.3-.6a7 7 0 0 0 2.4 1.4l.46 2.9h3.2l.46-2.9a7 7 0 0 0 2.4-1.4l2.3.6 2-3.4-1.9-1.5c.09-.46.14-.92.14-1.4z" />
    </>
  ),
  'log-out': (
    <>
      <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
      <path d="M16 17l5-5-5-5M21 12H9" />
    </>
  ),
  menu: <path d="M4 7h16M4 12h16M4 17h16" />,
  plus: <path d="M12 5v14M5 12h14" />,
  'arrow-right': <path d="M5 12h14M13 6l6 6-6 6" />,
  x: <path d="M6 6l12 12M18 6L6 18" />,
  'credit-card': (
    <>
      <rect x="2.5" y="5.5" width="19" height="14" rx="2.5" />
      <path d="M2.5 10h19" />
    </>
  ),
  'alert-circle': (
    <>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 8v5M12 16.2v.1" />
    </>
  ),
  trash: (
    <>
      <path d="M4 7h16" />
      <path d="M9 7V4.5A1.5 1.5 0 0 1 10.5 3h3A1.5 1.5 0 0 1 15 4.5V7" />
      <path d="M6 7l1 13a2 2 0 0 0 2 1.8h6a2 2 0 0 0 2-1.8l1-13" />
      <path d="M10 11v6M14 11v6" />
    </>
  ),
  image: (
    <>
      <rect x="3" y="4" width="18" height="16" rx="2.5" />
      <circle cx="8.5" cy="9.5" r="1.7" />
      <path d="M21 16l-5.5-5.5a2 2 0 0 0-2.8 0L4 19" />
    </>
  ),
  copy: (
    <>
      <rect x="8.5" y="8.5" width="12" height="12" rx="2.2" />
      <path d="M15.5 8.5V5.7A2.2 2.2 0 0 0 13.3 3.5H5.7A2.2 2.2 0 0 0 3.5 5.7v7.6a2.2 2.2 0 0 0 2.2 2.2h2.8" />
    </>
  ),
  'external-link': (
    <>
      <path d="M14 4h6v6" />
      <path d="M10 14L20 4" />
      <path d="M18 13.5V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h5.5" />
    </>
  ),
}

const filledIcons: Partial<Record<IconName, true>> = { star: true, check: true }

interface IconProps extends SVGProps<SVGSVGElement> {
  name: IconName
  size?: number
}

export function Icon({ name, size = 18, strokeWidth = 1.7, className, ...props }: IconProps) {
  const filled = filledIcons[name]
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill={filled ? 'currentColor' : 'none'}
      stroke={filled ? 'none' : 'currentColor'}
      strokeWidth={filled ? undefined : strokeWidth}
      strokeLinecap="round"
      strokeLinejoin="round"
      className={className}
      {...props}
    >
      {paths[name]}
    </svg>
  )
}
