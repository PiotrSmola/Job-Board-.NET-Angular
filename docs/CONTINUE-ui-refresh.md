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
