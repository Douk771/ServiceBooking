# Handoff: Premium Redesign of Service-Booking Frontend

## Overview
Visual redesign of the existing multi-page service-booking web app (`service-booking-frontend`, React + Vite + Tailwind + React Router). The current UI uses an orange/purple Tailwind palette, emoji as icons, and a generic SaaS look. This redesign moves it to a restrained, premium "beauty brand" aesthetic: warm cream background, espresso-ink text, a muted gold accent, a serif/sans type pairing, and a real line-icon set in place of emoji.

## About the Design Files
The files in this bundle (`*.dc.html`) are **design references built in HTML** — static/lightly-interactive prototypes showing the intended look, layout, and behavior. They are **not production code to copy in directly**. The task is to **recreate this design inside the existing React + TypeScript + Tailwind + React Router codebase**, reusing its data-fetching (`@tanstack/react-query`), forms (`react-hook-form`), and routing structure exactly as they are today — only the visual layer (Tailwind classes, colors, typography, icons, component markup) changes.

Concretely: keep every component's props, query keys, mutations, and route paths in `App.tsx` unchanged. Update `tailwind.config.js` (colors, fonts, radii), `index.css` (font import, base styles), and the JSX/className of each page/component listed below.

## Fidelity
**High-fidelity.** Colors, typography, spacing, and component layout in the bundled HTML are final — recreate them pixel-for-pixel using Tailwind utility classes (arbitrary values where the token doesn't already exist in the scale, e.g. `bg-[#FAF6F0]`). Prefer adding the tokens to `tailwind.config.js` (see Design Tokens below) and using the resulting utility names instead of hardcoding hex values everywhere.

## Design Tokens

### Colors
| Token | Hex | Use |
|---|---|---|
| `cream` | `#FAF6F0` | page background |
| `cream-deep` | `#F1E9DC` | alternate section bg, pill-nav track, badges |
| `ink` | `#2B2420` | primary text, headings, primary button bg |
| `ink-soft` | `#6B5F52` | secondary/body text |
| `muted` | `#A69A88` | tertiary text, meta labels, placeholders |
| `line` | `#E6DCCB` | borders, dividers |
| `line-strong` | `#D8C9AE` | hover borders, stronger dividers |
| `gold` | `#A9835A` | links, accents |
| `gold-dark` | `#8F6C46` | link/accent hover, icon tint |
| `white` | `#FFFFFF` | cards |
| `success` text/bg | `#4B6B4B` / `#E9F1E6` | confirmed/active states |
| `danger` text/bg | `#A6503A` / `#FBEAE4` | cancel actions, cancelled state |
| `warning` text/bg` | `#8A6A2E` / `#FBF3E1` | pending/attention states |
| `info` text/bg | `#52667A` / `#E9EEF3` | completed/neutral state |

### Typography
- Display/serif (headings only): **Newsreader** — weights 400, 500, 600, italic 400/500. Google Fonts.
- Body/UI: **Inter** — weights 400, 500, 600, 700. (Already the project's font — keep it.)
- Headline sizes: hero 60px/1.08, page h1 30px, section h2 24–32px, card title 16–19px — all serif, weight 500.
- Body copy 14–15px/1.55–1.6 in Inter, `ink-soft` color.

### Radius
- Cards/sections: 16–24px (`rounded-2xl`/`rounded-3xl`, or arbitrary `rounded-[20px]`).
- Buttons and pills (all buttons, tab switchers, badges): fully round, `rounded-full`.
- Inputs: 12px.
- Small icon-avatar squares: 14–18px.
- Circular avatars: `rounded-full`.

### Shadows
- Resting card: none or `shadow-sm`.
- Hover card: `0 14px 34px rgba(43,36,32,0.10)` + `translateY(-3px)`.
- Modal: `0 30px 70px rgba(43,36,32,0.25)`.

### Icons
Emoji are fully replaced with a custom minimal line-icon set: 24×24 viewBox, `stroke="currentColor"`, `stroke-width 1.6–1.8`, `stroke-linecap/linejoin="round"`, `fill="none"` (star icons are filled). Icons used: calendar, clock, map-pin, phone, mail, star (outline + filled), check, check-circle, chevron (left/right/down), search, user, users, scissors (brand mark + service icon), briefcase/store (company icon), bar-chart, megaphone, settings, log-out, menu, plus, arrow-right, x (close), credit-card, alert-circle. Exact SVG paths are inline in the HTML files — copy them into a shared `Icon.tsx`/icon-sprite in the codebase rather than re-authoring.

## Screens / Views

### 1. Home — `Home.dc.html` → replaces `src/pages/HomePage.tsx`
- Sticky nav, 76px tall, `cream` bg at 86% opacity + `backdrop-blur`, bottom border `line`. Left: circular ink-bg brand mark (scissors icon) + serif wordmark "ServiceBooking". Right: nav links + pill primary button ("Регистрация": `ink` bg, `cream` text).
- Hero: 2-column grid (1.05fr/0.95fr, 64px gap). Left: eyebrow pill (border `line-strong`, `gold-dark` text, check-circle icon), serif h1 60px with italic gold-dark accent line, soft body paragraph, primary pill CTA (ink bg) + text link with underline. Right: large rounded (28px) placeholder photo — use an `<image-slot>`-equivalent (a real `<img>` once photography exists) at 4:5 aspect ratio.
- "How it works" band: `cream-deep` bg, 3-column grid, each column: 46px circle icon badge (`cream` bg, `line-strong` border) + serif h3 21px + soft body text.
- Companies grid: section heading (serif 32px) + subtitle + a pill search input (right-aligned, search icon). Grid `repeat(auto-fill, minmax(320px,1fr))`, 24px gap. Each card: white bg, `line` border, 20px radius, 26px padding, hover = shadow + translateY(-3px) + `line-strong` border. Card content: 56px rounded-16 icon-avatar (store icon) + serif name (19px) + star rating row; 2-line-clamp description; footer row with map-pin + city, and a green "Онлайн-запись" pill badge (check icon) shown only when the company allows online booking.
- Footer: `cream-deep` bg, wordmark left, copyright right.

### 2. Login — `Login.dc.html` → replaces `src/pages/LoginPage.tsx`
- Centered single column, max-width 420px. Brand mark + wordmark above a white card (24px radius, `line` border, soft shadow, 44px padding).
- Card: serif h1 28px "С возвращением", soft subtitle, phone + password inputs (12px radius, `line` border, gold focus ring `0 0 0 3px cream-deep`), full-width ink pill submit button, footer link to Register.

### 3. Register — `Register.dc.html` → replaces `src/pages/RegisterPage.tsx`
- Same shell as Login. Two-column first/last name row, phone, optional email, password, ink pill submit, footer link to Login.

### 4. Company (public booking page) — `Company.dc.html` → replaces `src/pages/CompanyPage.tsx` + `src/components/booking/BookingModal.tsx`
- Condensed nav (brand + "Мои визиты" link + circular initials avatar).
- Company header: white card, 24px radius; full-bleed cover placeholder photo (220px, 18px radius) with a 64px store-icon avatar overlapping the bottom edge (white 4px border, -52px margin-top); name (serif 30px), description, address/phone rows with icons.
- Services list: vertical stack of cards (18px radius, `line` border): 52px icon-avatar + name/description/duration+price, primary pill "Записаться" button on the right that opens the booking modal for that service.
- Reviews: heading + average-rating pill (filled star + score + count), stacked review cards with reviewer name, service, star row, comment, date.
- **Booking modal** (opens on "Записаться"): centered overlay (`rgba(43,36,32,0.45)` + blur), white/cream card 440px wide, 26px radius. Header: service name/meta + close (x) button + 4-segment progress bar (ink-filled up to current step). Steps, each replacing the prior in place (no route change): **Master** (list of clickable rows: avatar-initials + name + bio + chevron) → **Date** (2-col grid of day buttons, "Сегодня"/"Завтра" labels then weekday+date) → **Time** (3-col grid of time-slot buttons) → **Info** (booking summary card + name/phone inputs + confirm button) → **Done** (success check-circle icon, confirmation copy, close button). Each step (after the first) has a "← back" link at the top.

### 5. My Visits — `MyVisits.dc.html` → replaces `src/pages/ClientBookingsPage.tsx`
- Page h1, pill tab switcher (Все/Предстоящие/Завершённые/Отменённые) — same pill-track pattern as Cabinet.
- Bookings grouped by month (uppercase small-caps label), each a card row: date/time block (`cream-deep` bg, 14px radius) + service name + status pill (color per status token above) + company/master line + price; right-aligned action buttons (cancel = danger-tinted pill, review = outlined pill) shown conditionally per booking state.

### 6. Cabinet (staff/owner dashboard) — `Cabinet.dc.html` → replaces `src/pages/CabinetPage.tsx` + `src/pages/owner/DashboardTab.tsx`, `ScheduleTab.tsx`, `MailingTab.tsx`, and `src/pages/MasterClientsPage.tsx`
- Pill tab switcher: Дашборд / Мои компании / Расписание / Клиенты / Отчёты / Рассылка (same active-tab pattern: active = white bg + soft shadow, inactive = transparent + gold-dark text).
- **Дашборд**: 4-up stat tiles, a 7-bar CSS bar chart (gold-tan bars) for daily revenue, 2-col grid of "master workload" and "popular services" lists.
- **Мои компании**: card rows (icon-avatar, name, slug) + "Управление"/"Открыть" actions + create-company pill button.
- **Расписание**: master-picker chips row, then a month calendar card (7-col grid, working days tinted `cream-deep`, off days lighter, today ring-highlighted, prev/next month arrows) + legend row.
- **Клиенты**: search input + list of client cards (avatar-initials, name, last-visit/visit-count meta, phone).
- **Отчёты**: date-range pickers + "Построить отчёт" button, per-master earning rows (bookings count, commission %, total, master share), totals band (`cream-deep`).
- **Рассылка**: compose card (subject input, message textarea, send pill button) + history list cards (subject, message preview, recipient count, timestamp).

### 7. Profile — `Profile.dc.html` → replaces `src/pages/ProfilePage.tsx`
- Single column, max-width 640px. Stacked white cards: avatar+roles (72px avatar-initials + name/contact + role pill), personal-data form (first/last name + save), plan/subscription card (plan name, price, paid-until, feature pills: online-booking/mailing/analytics — enabled=green, disabled=muted), password-change form.

### 8. Admin (super-admin console) — `Admin.dc.html` → replaces `src/pages/AdminPage.tsx` (+ `src/pages/admin/PlansTab.tsx`, simplified)
- Same pill tab pattern: Дашборд / Компании / Пользователи / Записи / Тарифы.
- **Дашборд**: 4-up centered stat tiles (large number + label, no icons).
- **Компании**: search + row cards (icon-avatar, name, plan badge, owner/members/bookings meta, "Сменить владельца" action).
- **Пользователи**: search + compact row cards (avatar, name, role chip, contact, "Роли" action).
- **Записи**: date-range filters + "Найти" + booking rows with status pill and price.
- **Тарифы**: 3-up plan cards (name, price, subscriber count).

## Not covered by these mockups
The following existing screens were **not** redesigned in this pass — apply the same tokens/patterns above to them when the developer gets to them: `MyBookingsPage.tsx` (master's own booking-management list — very similar to My Visits + Cabinet schedule, reuse those patterns), `src/pages/owner/CompanyManagePage.tsx` (services/members/settings tabs — reuse Cabinet's card and tab patterns), `EmbedPage.tsx` (reuse Company page's service-card pattern in a narrower shell).

## Interactions & Behavior
- **Booking modal** (Company page): step state machine `master → date → slot → info → done`, back-navigation between steps, progress bar reflects step index. This is real interaction in the HTML prototype (vanilla state) — reimplement with the existing `BookingModal.tsx` state logic (`Step` union type), only the JSX/classNames change.
- **Tab switchers** (My Visits, Cabinet, Admin): simple local `useState` string, same as today's implementation — only restyle the pill track/active state.
- Hover states: cards lift (`translateY(-3px)`) and gain a stronger border + shadow; nav links and text links go from `ink-soft`/`gold` to `gold-dark`/`ink` on hover.
- No new loading/error/responsive behavior was specified beyond what exists today — keep the current skeleton-pulse loading blocks (`animate-pulse`) but recolor them to `cream-deep` instead of `gray-100`.

## State Management
No new state was introduced. Reuse the existing `@tanstack/react-query` queries/mutations, `react-hook-form` forms, and `zustand` auth store exactly as in the current codebase — this is a styling/markup pass only.

## Assets
- Fonts: Newsreader + Inter, both via Google Fonts (`<link>` in `index.html`, or self-hosted if the project prefers).
- Photos: none supplied. All photo areas in the mockups are drag-and-drop placeholders (company cover photo, hero photo) captioned "Фото: …" — swap for real `<img>` tags once photography/logos are available; until then keep a striped placeholder with a monospace caption in the same spot so empty states are self-explanatory.
- Icons: hand-authored inline SVGs (see Design Tokens → Icons). No external icon package was added — either inline the same SVGs as a small `icons.tsx` module, or swap in an equivalent from an existing icon library (e.g. Lucide) matching stroke width 1.6–1.8.
- Company/service/client/master names, addresses, phone numbers, and review text in the mockups are **explicit placeholders** ("Название компании", "Имя мастера", "Улица, дом, город", etc.) — these are not real data, just layout stand-ins; wire up the real API data already used by the current pages.

## Files in this bundle
- `Home.dc.html` — landing/company-directory page
- `Login.dc.html` — sign-in
- `Register.dc.html` — sign-up
- `Company.dc.html` — public company page + booking modal
- `MyVisits.dc.html` — client's booking history
- `Cabinet.dc.html` — staff/owner dashboard (6 tabs)
- `Profile.dc.html` — account profile
- `Admin.dc.html` — super-admin console
- `image-slot.js` — the web component behind the photo placeholders in the mockups (reference only — not used in the production React app)

Open any `.dc.html` file directly in a browser to view it; each links to the others via relative `<a href>` for full click-through navigation.
