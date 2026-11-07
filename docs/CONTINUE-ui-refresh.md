# UI refresh (feature/ui-refresh) – spec for agents

Approved mockups: `.mockups/ui-refresh/*.dc.html` in the repo root (git-ignored, readable in containers at `/repo/.mockups/ui-refresh/`). Open them as plain HTML/CSS reference – markup between `<x-dc>` tags, styles in `<helmet><style>` and inline. Canvas: https://claude.ai/artifact/QqD91fh6EaXKUScPrNdW4P (private, owner only).

Goal: a calm refresh, not a redesign. Keep Angular Material components, cut "AI design" tropes (pills/tags everywhere, borders on every container, dividers, glow, gradients, ALL CAPS, eyebrows, periods in titles, verbose text), improve readability. Must not look like pracuj.pl / justjoin.it.

## Design tokens (CSS custom properties on `:root`, used everywhere)
- Background `#121417`, top bar `#16191d`, surface (cards) `#1b1e23`, raised/hover `#20242a`, selected/nav active `#262a30`, input background `#16191d`, input border `#3d434b`, hairline `#22262b` (rare).
- Text `#e9ebee`, secondary `#a7adb5`, tertiary `#8b929b` (min contrast 4.5:1 on all surfaces – verify), link/accent `#5fd4e6` (hover `#8fe3ef`).
- One accent only: cyan `#5fd4e6`. Filled primary button: background `#5fd4e6`, text `#0d1b1e`. Remove magenta/violet accent, all glows (`box-shadow` glows, `text-shadow`), gradients (except the offer hero mask), `fadeIn` gimmicks.
- Status colours (dot + text label, never colour alone): submitted `#8fb3ff`, in review `#e3b15a`, interview `#5fd4e6`, offered `#8fd18f`, hired `#6fcf97`, rejected `#f28b82`, withdrawn/closed/draft `#8b929b`; danger text `#f2a39c`; warning banner background `#2a2417` text `#f3dfb5`.
- Type: Roboto, sentence case, no letter-spacing, no uppercase. h1 28px/500 (offer title 32px), h2 18–20px/500, body 14–15px/1.5. Numbers in tables `font-variant-numeric: tabular-nums`.
- Shapes: cards radius 12 (blocks 16, dialogs 20), buttons radius 20 height 40, inputs radius 8 height 44. Cards = fill only, no borders. Focus: `outline: 2px solid #5fd4e6; outline-offset: 2px` on `:focus-visible`. `prefers-reduced-motion` respected.
- Navbar: 72px high, logo "Job Board" 22px bold as a real link, nav links 16px (10px 14px padding, active = `#262a30` background), user button 16px with initials avatar; on mobile labels stay accessible (no `display:none` on button labels – use visually-hidden).

## Per view (see the matching mockup)
- `Main.dc.html` – job list, variant A: search row, left filter sidebar (fieldset/legend, counts right-aligned in tertiary colour), active filters as plain text + "Wyczyść filtry", result count + sort, offer cards (title link 17px, company · city, meta line "Senior · B2B · Zdalnie", skills as plain comma text, salary right, posted date, bookmark icon button), pagination.
- `OfertaB.dc.html` – offer details, variant B: hero with company background image + dark gradient mask (`rgba(18,20,23,.7)` → `.82` → `#121417`; dim % from company settings, min 55), back link, title 32px, company link · city · date, save + "Aplikuj" (anchor to the inline apply section); facts block overlapping the hero bottom (salary 28px full row with "netto miesięcznie" etc., other facts 20px); sections as rows (title left 200px, text right); inline "Twoja aplikacja" section at the end (profile documents, add files, message, send) instead of the modal; "Podobne oferty" list. Without a company background: same layout, plain `#16191d` hero.
- `Logowanie.dc.html` – login + register cards, account type toggle, password strength as text, error in `role="alert"`, demo accounts in `<details>`.
- `MojeAplikacje.dc.html` – tabs "W toku"/"Zakończone", expandable rows with timeline + attachments + actions.
- `Konto.dc.html` – single page with side navigation (Profil, Dokumenty, Hasło i bezpieczeństwo, Powiadomienia) instead of tabs; e-mail confirmation banner as a calm warning strip.
- `Firmy.dc.html` – companies grid + public profile with photo gallery (1 large + 4 small, "+N zdjęć" tile opens the full gallery dialog), offers list.
- `PanelPracodawcy.dc.html`, `PanelPracodawcyProfil.dc.html` – employer panel tabs, offers table with status dots, status change dialog; profile tab: company data form, photos management (grid with caption/edit/delete, "Dodaj zdjęcia" tile, drag to reorder – keyboard alternative: move up/down buttons), offer background (live preview with mask, change/remove image, dim slider 55–95, focal point select).
- `PanelAdmina.dc.html` – admin tabs, stats: segmented range, KPI tiles, line chart, funnel bars, companies to verify table.
- Screens not mocked (forgot/reset/confirm password, saved jobs, categories tab, dialogs): apply the same tokens and patterns.

## Quality (mandatory)
WCAG 2.2 AA (contrast checked, keyboard, focus visible, landmarks `header`/`nav`/`main`, skip link, page titles via `TitleStrategy`, focus + announcement after navigation, `lang="pl"`, Polish `MatPaginatorIntl`, icon buttons with `aria-label`, reflow at 320px – `minmax(min(Xpx,100%),1fr)`), no memory leaks (`takeUntilDestroyed`, timers cleared), Core Web Vitals (OnPush everywhere, lazy routes, reserved space for async content, images with width/height), no `innerHTML`. Initial bundle should go down, not up: use selective Material themes instead of `all-component-themes`.

## New backend features (contract)
Storage: existing `IFileStorage`; images validated by content (PNG/JPEG only, ≤ 5 MB each, read width/height from the header and store them), keys `companies/{companyId}/photos/{guid}.{ext}` and `companies/{companyId}/offer-background/{guid}.{ext}`; files deleted after commit when records are removed (also when the company is deleted).

Company photos (`CompanyPhoto { id, companyId, fileKey, contentType, size, width, height, caption (≤150, nullable), sortOrder, uploadedAt }`, max 20 per company):
- `GET /api/companies/{id}/photos` (public for approved companies; members/admin always) → `[{ id, caption, width, height, url }]` ordered by `sortOrder`; `url` = `/api/companies/{id}/photos/{photoId}/file`.
- `GET /api/companies/{id}/photos/{photoId}/file` → image, `Content-Disposition: inline`, `X-Content-Type-Options: nosniff`, `Cache-Control: public, max-age=86400` (approved companies) / `private, no-store` otherwise.
- Employer (own company): `POST /api/employer/company/photos` multipart `files` (all-or-nothing, limit check under the company row lock) → 201 list; `PATCH /api/employer/company/photos/{photoId}` `{ caption }`; `PUT /api/employer/company/photos/order` `{ photoIds: [...] }` (must contain exactly the company's photos) → 200 list; `DELETE /api/employer/company/photos/{photoId}` → 204.
- Admin moderation: `DELETE /api/admin/companies/{id}/photos/{photoId}`.
- Messages: `A company can have at most 20 photos`, `Unsupported image type - upload a PNG or JPEG file`, `File is too large - maximum size is 5 MB`, `Photo not found`, `Invalid photo order`.

Offer background (on `Company`: `OfferBackgroundKey`, `OfferBackgroundContentType`, `OfferBackgroundDim` int 55–95 default 75, `OfferBackgroundFocus` enum `Center=0, Top=1, Bottom=2`):
- Employer: `PUT /api/employer/company/offer-background` multipart `file` → 200 `{ url, dim, focus }`; `PATCH /api/employer/company/offer-background` `{ dim, focus }` (dim outside 55–95 → 400) → 200; `DELETE` → 204. Admin: `DELETE /api/admin/companies/{id}/offer-background`.
- `GET /api/companies/{id}/offer-background/file` → image (same headers as photos).
- `JobOfferDto` += `companyBackground: { url, dim, focus } | null` (null when the company has none or is not approved). `CompanyDto` += `photosCount`, `offerBackground` (same shape).
- Upload endpoints: `[RequestSizeLimit]` 26 MB, the existing `uploads` rate limit, ownership checked before reading the body; nginx 26m location extended to `^/api/employer/company/(photos|offer-background)`.
- Seeder: no photos for demo companies (optional: none). README in English.

## Foundation – how to use
The shared base for the view agents (theme, shell, navbar, shared components). Rebuild views on top of it – do not re-declare colours, fonts or Material overrides in component styles.

**Tokens** (`frontend/src/styles.scss`, `:root`) – always `var(--…)`, no hex values in components:
- Surfaces: `--color-bg`, `--color-topbar`, `--color-surface` (cards), `--color-raised` (hover, dialogs, menus), `--color-selected` (active nav/tab, chips), `--color-input-bg`, `--color-hairline` (rare separators), `--color-avatar`.
- Text: `--color-text`, `--color-text-prose` (long text, `#d4d8dd`), `--color-text-secondary`, `--color-text-tertiary` – all ≥ 4.5:1 on every surface incl. `#262a30`. Accent: `--color-accent`, `--color-accent-hover`, `--color-on-accent`. Danger/warning: `--color-danger` (text), `--color-danger-strong` (filled), `--color-warning-bg` / `--color-warning-text` / `--color-warning-icon`.
- Status: `--status-submitted|review|interview|offered|hired|rejected|neutral`.
- Type & shape: `--font-size-sm` 13, `--font-size-body` 14, `--font-size-lg` 16, `--font-size-h1` 28, `--font-size-h2` 18; `--radius-card` 12, `--radius-block` 16, `--radius-dialog` 20, `--radius-input` 8, `--radius-button` 20; `--page-max-width` 1120, `--page-gutter` 24 (16 under 600px), `--header-height` 72, `--focus-ring`.
- Deviation from the mockups: `--color-input-border` is `#6b727b` (mockup `#3d434b` has 1.7:1 – WCAG 1.4.11 needs 3:1 for input boundaries). Outlined buttons keep the mockup `#4a5058` (their text identifies them).
- Legacy names (`--primary-color`, `--text-secondary`, `--surface-color`, `--border-color`, `--secondary-color`, `--primary-glow` …) are DEPRECATED aliases so un-rebuilt views already use one accent (glows resolve to `transparent`). Replace them when you rebuild a view; the last agent removes the aliases block.

**Material theme** – `mat.theme()` (system tokens only, no `all-component-themes`) + `mat.theme-overrides` mapped to the tokens + `mat.*-overrides` for buttons, icon buttons, form fields, cards, tabs, tables, dialogs, menus, select/autocomplete panels, snack bars, tooltips, paginator, chips, datepicker and button toggles. So, with no component CSS:
- `mat-flat-button`/`mat-raised-button` = filled cyan, no shadow; `mat-stroked-button` = thin outline, light text; `mat-button` = cyan text; `color="warn"` = danger (red text / filled `#f28b82`); `color="accent"` looks like primary (one accent). Height 40, radius 20, no letter-spacing.
- Form fields: use `appearance="outline"`; height 44, radius 8, background `--color-input-bg`, focus = cyan outline. Keep the default subscript (reserved space for hints/errors – no CLS).
- `mat-card` = `--color-surface`, radius 12, no border, no shadow. Tabs: 48 px, cyan 2 px indicator, hairline divider. `mat-table`: transparent background, 13 px tertiary headers, hairline row separators. Dialogs: `--color-raised`, radius 20. Menus/select panels: `--color-raised`, radius 8. Snack bar: `#262a30` with a cyan action.
- `mat-paginator`: add `providers: [providePolishPaginatorIntl()]` (`services/polish-paginator-intl.ts`) to every component that renders one – it is deliberately NOT in `app.config.ts` (importing the paginator there pulls ~180 kB into the initial bundle).

**Global styles** – base `body` 14px/1.5 Roboto, `h1` 28/500, `h2` 18/500, `h3` 16/500, sentence case, no letter-spacing/uppercase. `:focus-visible` = 2px cyan outline with 2px offset everywhere (inputs inside Material form fields excluded – the field shows focus). `prefers-reduced-motion` turns off animations/transitions (progress indicators keep moving). Do not add glows, gradients (except the offer hero mask), `text-shadow`, `fadeIn` or uppercase eyebrows.

**Helper classes** (minimal set):
- `.page` – page container (max 1120, gutter, 40px top / 64px bottom). Use it as the root of every routed view.
- `.page-header` (flex row, wraps) with the `h1` (+ optional `.page-subtitle`) as the first child and `.page-actions` for buttons on the right.
- `.surface` – card (fill only, radius 12, 20/24 padding); `.block` – bigger block (radius 16, 28 padding); `.surface-interactive` – hover/focus-within highlight for clickable cards.
- `.section-title` (h2 inside a page/card), `.muted` (secondary), `.tertiary`, `.small` (13px), `.danger-text`, `.num` (tabular numbers; on `td`/`th` also right-aligned), `.text-link` (underlined link inside running text – colour alone is not enough, WCAG 1.4.1), `.warning-strip` (calm warning box), `.visually-hidden` (`.sr-only` is an alias – drop local copies when rebuilding), `.full-width`, legacy `.container`.

**Shell** (`app.ts`/`app.html`) – skip link „Przejdź do treści”, `<header>` with the navbar and the e-mail banner slot, `<main id="main" tabindex="-1">`. Do not add your own `<main>`/`<header>` in views (use `<section>`/`<article>` with headings). After a path change the shell scrolls to the top, focuses `main` and announces the title (`LiveAnnouncer`); query-param or fragment changes (filters, paging, `#apply`) do not move focus or scroll.
- Page titles: every route has `title` in `app.routes.ts` (`AppTitleStrategy` → „Oferty pracy – Job Board”). New routes must get a Polish `title`. Routes whose title comes from data have `data: { [DYNAMIC_TITLE]: true }`; their view calls `PageTitleService.set(text)` after loading – also on error (e.g. „Oferta niedostępna”); `set()` sets the title and announces it. Already wired in `job-detail` (offer title) and `company-detail` (company name) – keep these calls when rebuilding.
- Navbar: 72px, logo link, links with `routerLinkActive="active"` + `ariaCurrentWhenActive="page"`, account button with initials and `aria-label="Menu konta: <name>"`; under 768px links show icons and keep labels visually hidden. Candidate nav: Oferty, Firmy, Moje aplikacje, Zapisane; employer: Panel pracodawcy; admin: Panel admina; account menu: e-mail, Moje konto, Wyloguj.

**Statuses** – always `<app-status-chip [label] [tone]>` with a `StatusPresentation` from `utils/status-labels.ts` (dot + text label, text in the normal text colour – never colour alone). Tones: `info` submitted (blue), `warning` in review / pending / expired (amber), `accent` interview (cyan), `positive` offered (green), `success` hired / active / approved, `danger` rejected, `muted`/`neutral` withdrawn / closed / draft (grey). Do not invent new chip/pill styles; for a status in a table cell just put the component in the cell.

**Patterns** (see the mockups):
- Card list (offers, applications, companies): `article.surface` (or `.surface.surface-interactive` when the whole card is a link) – title 17px/500 as a link in `--color-text`, secondary line `company · city`, meta line „Senior · B2B · Zdalnie”, skills as plain comma text in tertiary 13px; right column: salary (500), date (tertiary 13px), icon button. Gap 12px between cards. No borders, no chips.
- Sections inside a page: `h2.section-title` + content, or the offer-details row pattern (`grid-template-columns: minmax(0,200px) minmax(0,1fr)`, title left, text right, a hairline `box-shadow: inset 0 1px 0 var(--color-hairline)` between rows, single column on mobile).
- Grids: `grid-template-columns: repeat(auto-fill, minmax(min(280px, 100%), 1fr))` (reflow at 320px).
- Tables: `mat-table` (theme already done) inside a `.surface` with `overflow-x: auto` on a wrapper; numeric columns `.num`; actions as icon buttons with `aria-label` naming the row („Edytuj: <tytuł>”).
- Dialogs: `mat-dialog-title` (sentence case, no period), `mat-dialog-content`, `mat-dialog-actions align="end"` with a text button „Anuluj” and one filled primary action; autofocus the first field; errors in `role="alert"`.
- Empty/loading states: reserve the final height (skeleton or `min-height`) to avoid CLS; spinners with `aria-label` or a visible text.
- Shared components already restyled with tokens (logic unchanged): `app-status-timeline` (date column + status + comment), `app-attachment-list`, `app-attachments-picker`, `app-profile-documents-picker`, the e-mail confirmation banner (`min-height`, wraps, no line clamp).
- Leftovers for view agents (in view SCSS, not touched by the foundation): uppercase/letter-spacing headings (admin tabs, company list, job list/detail), primary→secondary gradients (job list/detail), glow `box-shadow`s (admin jobs tab), `fadeIn` in `admin-panel/_admin-table.scss`, local `.sr-only` copies, `Eager` change detection.
