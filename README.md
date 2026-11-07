# Job Board

A job board with candidate, employer and admin roles, built with ASP.NET Core 9 and Angular 22 as a portfolio project that runs locally as a Docker stack.

It covers the whole recruitment loop: companies register and get verified, publish offers with a category, level, contract types and skills, candidates search with faceted filters, apply with attachments and follow the status of their applications, and both sides get e-mail notifications and statistics. It is not deployed anywhere and is not meant to be - the stack listens on `127.0.0.1` only.

## Features

| Role | What they can do |
| --- | --- |
| Candidate (`User`) | Search offers with filters and facet counts, save offers, apply with up to 10 attachments (CV, cover letter, portfolio, other) or with documents from the profile, follow the status history of each application and withdraw it, manage the profile, profile documents, notification settings, password and account deletion |
| Employer (`Employer`) | Register together with a company, edit the company profile (sent for verification), add a photo gallery and an offer background image, manage the company's offers (drafts, publishing, expiry dates, closing), review applications and download attachments, move applications through the recruitment stages with comments visible to the candidate, see the company statistics with offer views and export them to CSV |
| Admin (`Admin`) | Everything an employer can do for every company, plus: approve or reject companies, remove company photos and backgrounds, manage categories, see platform-wide statistics, delete applications and offers with their files, reset the demo data |

Public pages (no account needed): the job list with filters, facets and URL-driven state (links and the back button work), offer details, the company list and company profiles. Only approved companies and their active, unexpired offers are public.

After signing in (without a `returnUrl`) an employer lands on the `/employer` panel and everyone else on the job list. "My applications" (`/my-applications`) is for candidates only - an employer is redirected to `/employer` and an admin to `/admin`. Only candidates apply to offers - employers and admins cannot.

## Tech stack

| Area | Technology |
| --- | --- |
| Backend | ASP.NET Core 9 (controllers), ASP.NET Core Identity, JWT bearer authentication, built-in rate limiting |
| Data | MySQL 8.4 (LTS), Entity Framework Core 9 with Pomelo.EntityFrameworkCore.MySql 9.0, MySqlConnector |
| File storage | SeaweedFS 4.50 (S3-compatible API) through AWSSDK.S3; local disk outside Docker |
| E-mail | MailKit over SMTP, transactional outbox in MySQL, Mailpit as the local mailbox |
| API docs | Swashbuckle (Swagger UI) |
| Frontend | Angular 22 (standalone components, signals, `@if`/`@for`, `@defer`), Angular Material 22, TypeScript 6.0, RxJS |
| Charts | Hand-written SVG components (no chart library) |
| Web server | nginx (serves the Angular build and proxies `/api` to the backend) |
| Tests | xUnit with `WebApplicationFactory` on a real MySQL server (backend), Vitest through the Angular CLI (frontend) |
| Tooling | Docker Compose v2, PowerShell start script (`docker-up.ps1`) |

## Why these technologies

**Pomelo and .NET 9.** MySQL is reached through `Pomelo.EntityFrameworkCore.MySql`, the most complete EF Core provider for MySQL (proper `utf8mb4` collations, `INSERT IGNORE` / `ON DUPLICATE KEY` friendly SQL, MySQL-specific column types). The newest stable Pomelo (9.0.0) supports only EF Core 9, so the project stays on .NET 9 (`net9.0`, ASP.NET Core and EF Core 9.0.x). Moving to .NET 10 (LTS) waits for a stable Pomelo release that supports EF Core 10.

**SeaweedFS instead of MinIO.** Attachments live in an S3-compatible store so that the backend talks to the same API it would use in the cloud. SeaweedFS is a small, actively maintained Apache-2.0 server with an S3 gateway and a pinned Docker image; MinIO's community edition has been cut back (no admin console, no official prebuilt images), which makes it a poor fit for a reproducible local stack. The backend only knows `IFileStorage`, so the store can be swapped (`Storage:Provider`).

**An e-mail outbox instead of Hangfire.** Each e-mail is written to the `EmailMessages` table in the same transaction as the event that causes it (a registration, a status change), and a hosted service sends it with retries. That gives "the e-mail goes out if and only if the change was committed" without a job scheduler, its dashboard and its extra tables - the project needs one kind of background job, not a general-purpose queue.

**Mailpit.** A local SMTP server with a web UI: every e-mail of the application (confirmation links, password reset links, notifications) can be opened in the browser, and nothing is ever sent outside. It also has an HTTP API used by the real SMTP test.

**Hand-written SVG charts.** The statistics dashboard needs a line chart, a funnel and KPI tiles. Small SVG components add no dependency, keep the lazily loaded statistics chunk small and are accessible by design - every chart is an image with a text summary (`role="img"` with an `aria-label`) and has a data table as an alternative, which a general chart library would not give for free.

## Quick start with Docker

Requirements: Docker Desktop (Docker Compose v2) and Windows PowerShell.

```powershell
.\docker-up.ps1
```

The script builds the images and starts five containers: MySQL, SeaweedFS (attachment storage), Mailpit, the backend and the frontend (nginx, which forwards `/api` to the backend). For every service it takes the preferred port and, if that port is taken, the next free one. The chosen ports are saved to `.env` and printed at the end:

| Service | Default address | Notes |
| --- | --- | --- |
| Frontend | `http://localhost:4200` | the application |
| Swagger UI | `http://localhost:5285/swagger` | the backend API; turned off with `SWAGGER_ENABLED=false` |
| Mailpit | `http://localhost:8025` | every e-mail sent by the application |
| MySQL | `localhost:3306` | `root` / `DB_PASSWORD` |
| SeaweedFS | not published | reachable only by the backend inside the compose network |

The preferred ports can be changed:

```powershell
.\docker-up.ps1 -FrontendPort 8080 -BackendPort 8081 -DbPort 3310 -MailpitPort 8026
```

On the first run the script generates the secrets and stores them in `.env` (outside the repository): `JWT_SECRET` (64 random characters), `S3_ACCESS_KEY` and `S3_SECRET_KEY` (20 and 40 letters and digits) and `ADMIN_PASSWORD` (20 characters). Later runs keep the existing values. The admin password is printed at the end (`Admin: admin@jobboard.com / ...`).

Stopping: `docker compose down` (the database stays in the `db-data` volume and the files in `storage-data`; `docker compose down -v` removes both).

The stack can also be started with `docker compose up -d --build` directly - the ports then come from `.env` (template: `.env.example`) or the defaults, and `.env` must contain `JWT_SECRET`, `S3_ACCESS_KEY`, `S3_SECRET_KEY` and `ADMIN_PASSWORD`.

### Running without Docker

Required versions:

- **.NET SDK 9.0**.
- **Node.js** `^22.22.3`, `^24.15.0` or `>=26.0.0` (required by Angular 22); the frontend Docker image builds on Node 24 LTS.
- **MySQL 8.4**.

Backend: `dotnet run` in `Backend` (needs MySQL on `localhost:3306`). In Development a built-in development JWT key is used when `JwtSettings:Secret` is not set; other environments require it (e.g. `JwtSettings__Secret`). Attachments go to `Backend/storage` by default. E-mails are sent only when `Email:Smtp:Host` is set - e.g. with a local Mailpit (`docker run -p 8025:8025 -p 1025:1025 axllent/mailpit:v1.31.1`): `Email__Smtp__Host=localhost`, `Email__Smtp__Port=1025`, `Email__Smtp__Security=None`.

Frontend: `npm start` in `frontend` - `ng serve` forwards `/api` to `http://localhost:5285` (`frontend/proxy.conf.json`).

## Configuration

### Docker stack (`.env`)

| Variable | Default | Meaning |
| --- | --- | --- |
| `FRONTEND_PORT`, `BACKEND_PORT`, `DB_PORT`, `MAILPIT_PORT` | `4200`, `5285`, `3306`, `8025` | host ports on `127.0.0.1`; `docker-up.ps1` replaces taken ones with the next free port. Mailpit's SMTP port (1025) is not published - only the backend uses it. Links in e-mails point to `http://localhost:${FRONTEND_PORT}` (`Frontend__BaseUrl`) |
| `DB_PASSWORD` | `jobboard` | the MySQL `root` password |
| `JWT_SECRET` | generated | the JWT signing key (at least 32 characters). Required - `docker compose` does not start without it. Changing it signs everyone out |
| `S3_ACCESS_KEY`, `S3_SECRET_KEY` | generated | the SeaweedFS keys. Required. Own values: letters and digits only, at least 16 characters (other values are replaced by the script with a warning). They are written into the SeaweedFS S3 config generated at container start, so the store rejects unsigned requests and a change applies after a restart of the stack |
| `ADMIN_PASSWORD` | generated | the password of the seeded admin accounts (`admin@jobboard.com`, `superadmin@jobboard.com`, `Seed__AdminPassword`). Required. Own value: letters and digits only, at least 16 characters, with a lowercase letter, an uppercase letter and a digit. It applies only to admin accounts created with it (a new database or a demo data reset) - **admin accounts in an existing `db-data` volume keep their current password** (e.g. `Admin123!` from older versions) until a demo data reset or `docker compose down -v` |
| `ALLOW_SEED_REFRESH` | `true` | whether an admin can reset the demo data from the panel (`POST /api/admin/refresh-seed` deletes **all** users, companies, offers, applications and files and recreates the seed data). Outside Docker the reset works only in Development or with `Admin:AllowSeedRefresh=true` |
| `SEED_DEMO_ACCOUNTS` | `true` | whether the seeder creates the demo candidate and employer accounts (`Seed__DemoAccounts`); with `false` only the admin accounts are created. Outside Docker the demo accounts are created only in Development unless `Seed:DemoAccounts` is set explicitly |
| `SWAGGER_ENABLED` | `true` | whether the backend serves Swagger UI (`Swagger__Enabled`). Outside Docker Swagger is available in Development, and in other environments only with `Swagger:Enabled=true` |

`docker-up.ps1` keeps the values of `ALLOW_SEED_REFRESH`, `SEED_DEMO_ACCOUNTS` and `SWAGGER_ENABLED` from `.env`.

### Backend settings (`appsettings.json`, environment variables)

| Setting | Meaning |
| --- | --- |
| `JwtSettings:Secret`, `JwtSettings:ExpirationInMinutes` | the HS256 signing key and the token lifetime (120 minutes) |
| `Seed:AdminPassword`, `Seed:DemoAccounts`, `Admin:AllowSeedRefresh` | see the `.env` table; outside Development a missing or too weak admin password stops the startup with a clear error |
| `Swagger:Enabled` | Swagger UI outside Development |
| `Storage:Provider` | `Local` (default) or `S3` |
| `Storage:LocalPath` | the directory for `Local`; default `<app directory>/storage` (`Backend/storage`, ignored by git) |
| `Storage:S3:ServiceUrl`, `Storage:S3:Bucket` | the S3-compatible endpoint (e.g. `http://storage:8333`) and the bucket (default `jobboard`, created at startup when missing) |
| `Storage:S3:AccessKey`, `Storage:S3:SecretKey` | the S3 keys (required for `S3`) |
| `Frontend:BaseUrl` | the frontend address used in e-mail links (default `http://localhost:4200`; must be an absolute http(s) URL) |
| `Email:Smtp:Host`, `Email:Smtp:Port` | the SMTP server; without a host e-mails wait in the outbox (a warning in the log) |
| `Email:Smtp:Security` | `None` (e.g. Mailpit), `StartTls`, `SslOnConnect`, `StartTlsWhenAvailable`, `Auto` (default) |
| `Email:Smtp:UserName`, `Email:Smtp:Password` | optional SMTP authentication |
| `Email:FromAddress`, `Email:FromName` | the sender (default `no-reply@jobboard.local`, "Job Board") |
| `Email:Outbox:*` | `PollIntervalSeconds` (10), `BatchSize` (20), `MaxAttempts` (8), `InitialRetryDelaySeconds` (30), `MaxRetryDelaySeconds` (3600), `SendTimeoutSeconds` (30), `RetentionDays` (30) |
| `RateLimiting:Auth:*` | `PermitLimit` (10), `WindowSeconds` (60), `ConcurrencyLimit` (4) |
| `RateLimiting:PublicLists:*` | `PermitLimit` (120), `WindowSeconds` (60) |
| `RateLimiting:AccountEmails:*` | `PermitLimit` (5), `WindowSeconds` (900) |
| `RateLimiting:Uploads:*` | `PermitLimit` (30), `WindowSeconds` (600), `ConcurrencyLimit` (8) |

Incomplete storage or e-mail configuration, an unknown storage provider and a rate limit below 1 stop the startup with a clear error.

## Demo accounts

Created on the first start and after a demo data reset. The passwords of the demo accounts are publicly known - they exist only for the local demo. Admin accounts are always created; the candidate and employer accounts only in Development or with `Seed:DemoAccounts=true` (on by default in the Docker stack).

| Role | E-mail | Password |
| --- | --- | --- |
| Admin | `admin@jobboard.com`, `superadmin@jobboard.com` | Development: `Admin123!`; Docker: `ADMIN_PASSWORD` from `.env` (printed by `docker-up.ps1`) |
| Candidate | `jan.kowalski@example.com` (also `anna.nowak@`, `piotr.wisniewski@`, `maria.wojcik@`, `tomasz.kaminski@example.com`) | `User123!` |
| Employer (approved company: TechCorp Solutions) | `rekruter@techcorp.pl` | `Employer123!` |
| Employer (pending company: Nowa Firma Sp. z o.o.) | `rekruter@nowafirma.pl` | `Employer123!` |

The seed also creates demo companies, offers, categories and skills. A demo employer account that exists without its company (e.g. after rolling `AddCompanies` back and forward) is linked to its company again at startup (a missing company is created). On startup the demo categories are created when none exist and demo offers without any taxonomy are filled in.

## How it works

### Roles and companies

The role is carried in the JWT (`role` claim), but the employer's company is read from the database on every request, so a changed assignment applies without signing in again. Resource access is checked in one place (`ICurrentUserAccess`): an admin manages everything, an employer only the offers and applications of their own company - another company's offer or application returns `404` (without revealing that it exists). Another company's employer still sees public offers (`GET /api/jobs/{id}`) like everyone else.

Company verification:

1. An employer registers with a company (`POST /api/auth/register-employer`, the "Rekrutuję" switch on the registration page). The account and the company are created in one transaction. Company names are unique regardless of case and diacritics (collation `utf8mb4_0900_ai_ci` - on purpose: "Zażółć" and "Zazolc" are the same brand); a taken name returns `400`. Before saving, invisible formatting and control characters (e.g. a zero-width space) are removed and all Unicode whitespace (including a non-breaking space) is collapsed to one space and trimmed; a name made only of such characters fails validation.
2. The company is **Pending**. The employer can already create and edit draft offers, but every write that would leave an offer active (`POST`/`PUT`/`PATCH` with status `Active`) returns `403` `Company must be approved before publishing offers`; drafts and closing are allowed. The panel then limits the offer form to "Draft" (plus the current status of the edited offer) and hides "Publish" / "Resume".
3. An admin approves the company (`POST /api/admin/companies/{id}/approve`) or rejects it with a reason (`POST /api/admin/companies/{id}/reject`). The employer sees the reason in the company profile.
4. A rejected company goes back to **Pending** when the employer saves the profile (`PUT /api/employer/company`, also without changes - the "Send again" button); the reason and the decision date are cleared.
5. An approved company goes back to **Pending** when the employer changes its name (other than case or whitespace) or website - this prevents impersonating another brand after verification. Until it is approved again, its profile and offers are not public (the panel warns before saving). Changing only the description or location keeps the status, and an admin's edit (`PUT /api/admin/companies/{id}`) never changes it.

Rejecting a company hides its offers from the list, the details and applying (`404`) without deleting them. An admin can also add an already approved company, edit it and delete it (only without offers and employer accounts). When creating an offer an admin must pass `companyId` (missing - `400` on `CompanyId`, unknown - `400` `Company not found`); for an employer it is ignored and the offer always belongs to their company. An employer account without a company (should not happen) gets `403` `Employer account is not linked to a company`.

`CompanyDto`: `status`, `rejectionReason` and `reviewedAt` are filled only for admins and company members (publicly `null`), `membersCount` only in `/api/admin/companies`; `activeJobsCount` is the number of publicly visible offers (`0` for an unapproved company). In the admin panel the company filter of the offers tab uses `/api/admin/companies` (all statuses), while the company picker in the offer form uses `/api/companies` (approved only).

The `AddCompanies` migration turned the text company name of offers into companies: for every name (whitespace-normalized, case-insensitive) an approved company was created with the creation date of its oldest offer and the spelling from that offer. Rolling it back restores the names on the offers (in the unified spelling); employer assignments and companies without offers are lost then.

### Offer lifecycle

An offer has a stored status and an optional expiry date (`expiresAt`, UTC):

| State | Meaning | Public list | Applying |
| --- | --- | --- | --- |
| Draft (`Draft`) | in preparation | no (details only for admins and the company's employers) | no |
| Active (`Active`) | published, without an expiry date or with one in the future | yes | yes |
| Expired | `Active` with an expiry date in the past - computed, not stored | no (details with a banner) | no |
| Closed (`Closed`) | recruitment finished | no (details with a banner) | no |

Admins and employers (own company only) manage offers in the panel (`GET /api/jobs/manage` with `state=draft|active|expired|closed`): quick actions Publish / Close / Resume (`PATCH /api/jobs/{id}/status`) and the status and "valid until" date in the offer form (an offer is valid until the end of the chosen day). An active offer cannot be published, resumed or saved with a new expiry date in the past - extend or close it instead; an expired offer can be edited without changing the date (it stays expired). `PUT /api/jobs/{id}` without `status` keeps the current status (`POST` without a status creates an active offer). Offers that existed before the lifecycle migration stay active without an expiry date.

Deleting an offer (`DELETE /api/jobs/{id}`): an admin deletes it with its applications, an employer only an offer without any applications (including withdrawn ones) - otherwise `400` `Job offer has applications - close it instead`; it can be closed instead.

### Job taxonomy, filters and facets

Every offer can have a category, a seniority level, contract types, a salary currency and period, and up to 15 skills. The job list (`GET /api/jobs`, and `GET /api/jobs/manage` for admins and employers) accepts these optional filters, all combined with AND:

| Parameter | Meaning |
| --- | --- |
| `searchTerm`, `location` | text search, up to 100 characters each |
| `category` / `categoryId` | category slug (`[a-z0-9-]`, up to 100 characters) and/or id |
| `seniority` | comma-separated levels (`0` intern, `1` junior, `2` mid, `3` senior, `4` lead, `5` manager); any of them matches |
| `contractTypes` | bit mask (`1` employment, `2` B2B, `4` mandate, `8` specific-task, `16` internship, `0..31`); an offer matches when it has any selected type; `0` means no filter |
| `workType` | comma-separated work types (`0,1`); a single number or name still works |
| `skills` | comma-separated skill names (up to 10, 50 characters each, case-insensitive); an offer must have all of them |
| `currency` | salary currency (`0` PLN, `1` EUR, `2` USD, `3` GBP); `0` is a real filter. `minSalary` / `maxSalary` are compared in the offer's own currency, without conversion |
| `orderBy` | `newest` (default), `oldest`, `salaryHigh`, `salaryLow`, `titleAsc`, `titleDesc`, `companyAsc`, `companyDesc`, `locationAsc`, `locationDesc` |

Invalid or oversized values return `400`. All filters run in SQL; the list loads category and skills in the same query and is backed by the composite index `(Status, ExpiresAt, CreatedAt)`. Lists are paged with a `Pagination` header (at most 50 items per page, page numbers capped at 100,000 - a page past the end is empty, `200`).

`GET /api/jobs/facets` (public, the same filter parameters, no paging) returns `total` and the counts for categories, seniority, single contract-type bits, work types and the 20 most common skills. The counts of a facet ignore that facet's own filter (so the selection can be widened) and apply all the others; values with a zero count are omitted. `GET /api/categories` lists the categories with the number of publicly visible offers, and `GET /api/skills?search=&limit=` suggests skills used by publicly visible offers (substring match, prefixes first, then by popularity; `limit` 1-20, default 10). Admins manage categories with `POST/PUT/DELETE /api/admin/categories`; deleting a category keeps its offers and clears their category.

When an offer is created or updated (`PUT` replaces all taxonomy fields), unknown skills are created automatically and matched to existing ones case-insensitively; a skill name cannot contain a comma (the list separator in filters). The `AddJobTaxonomy` migration gives existing offers PLN, monthly salaries, no contract types, no level and no category.

### Recruitment workflow

Every application has a status, and every change (including submitting) goes to a history with the date, the author and an optional comment:

`Submitted` → `InReview` → `Interview` → `Offered` → `Hired`, at any point `Rejected`; the candidate can also withdraw (`Withdrawn`).

- **The recruiter (an admin or an employer of the offer's company)** changes the status in the panel (`PUT /api/jobapplications/{id}/status`) and can add a comment - **the comment is visible to the candidate**. The same status with a comment stores a note without changing the status. A recruiter cannot set "Withdrawn", and a withdrawn application cannot be changed. Only an admin can delete an application.
- **The candidate** sees the status and history in "My applications" (without the authors of the changes) and can withdraw while the application is Submitted / InReview / Interview / Offered (`POST /api/jobapplications/{id}/withdraw`). After withdrawing they can apply again while the offer is active - the same application is reactivated with a new note and date.
- Only active, unexpired offers accept applications (applying to a draft returns `404` for a candidate, like a missing offer).
- Concurrent changes of the same application (including notes and withdrawal) are protected by a concurrency token: a request based on a stale state gets `409` `Application status was changed by another request`, and a concurrent re-application gets `400` `You have already applied to this job`.

### Application attachments

A candidate can attach several files - a CV, a cover letter, a portfolio or another document - when applying (the application is created first, then the files are uploaded in one request) or later in "My applications". Files can be added while the application is Submitted / InReview / Interview / Offered and removed at any time (also after a final decision and after withdrawal). The recruiter downloads them from the applications tab.

| Endpoint | Who | Result |
| --- | --- | --- |
| `POST /api/jobapplications/{id}/attachments` (multipart: `files` and parallel `kinds` fields) | application owner | `201` with the application (the `attachments` list) |
| `POST /api/jobapplications/{id}/attachments/from-profile` `{ documentIds }` | application owner | `201`; copies profile documents as attachments (the same limits and statuses as an upload) |
| `DELETE /api/jobapplications/{id}/attachments/{attachmentId}` | application owner, any status | `204`; missing - `404` `Attachment not found` |
| `GET /api/jobapplications/{id}/attachments/{attachmentId}` | owner, admin, employer of the offer's company | the file (`Content-Disposition: attachment; filename*=UTF-8''...`, `Content-Length`, `X-Content-Type-Options: nosniff`, `Cache-Control: no-store`) |

- Kind (`kinds`, a number): `0` CV, `1` cover letter, `2` portfolio, `3` other; the i-th `kinds` field belongs to the i-th file, a missing one means `3`, a value outside the list or more fields than files - `400` `Invalid attachment kind`. Every application in API responses (lists too) has `attachments: [{ id, kind, fileName, size, uploadedAt }]` (by kind and upload date) and `attachmentsTotalSize`.
- Allowed: PDF, DOCX, PNG and JPEG, up to 5 MB (5,242,880 B) per file, at most 10 files and 25 MB (26,214,400 B) per application (existing + new). The type is detected from the content (PDF - `%PDF-`; PNG and JPEG - signatures; DOCX - a ZIP archive with `[Content_Types].xml` and `word/` parts), not from the extension or the browser's content type; the server sets `Content-Type` and the extension. DOCX is never extracted: the end-of-central-directory record is checked first (no ZIP64 or multi-part archives, a directory of at most 256 KB ending right before that record, at most 1,000 entries), then the directory (at most 50 MB of declared uncompressed content) - otherwise the file is rejected as unsupported.
- All or nothing: all files of a request are validated first, then written to storage, and finally - in a transaction holding a row lock on the application, which enforces the 10-file and 25 MB limits under concurrent requests - the records are saved. When a concurrent request exceeds a limit, the stored files are deleted. When saving to the database throws, only files without a record are deleted (the exception may come after the commit); if that cannot be checked, the files stay (an orphaned file rather than a record without a file).
- Errors (`400`): `Unsupported file type - upload a PDF, DOCX, PNG or JPEG file`, `File is too large - maximum size is 5 MB`, `File is empty`, `Total size of attachments cannot exceed 25 MB`, `An application can have at most 10 attachments`, `No file uploaded - send files in the 'files' form field`, `Invalid multipart form data` (also a truncated request), `Attachments cannot be added in the current application status`. A request with `Content-Length` above 26 MB is rejected (`400` `Total size of attachments cannot exceed 25 MB`) without reading the body; in the Docker stack nginx allows up to 26 MB only on this endpoint, on `POST /api/account/documents` and on the company photo and offer background uploads (`client_max_body_size 26m`) and limits the rest of `/api/` to 1 MB.
- The owner and the status are checked before the server starts reading the body - another user's or a missing application (also for another company's employer) returns `404` at once.
- File names are sanitized (no path, control or formatting characters, no trailing dots or spaces, Windows reserved names such as `CON`, `NUL`, `COM1` prefixed with `_`, at most 255 characters, the extension matching the detected type) and are used only for display and download. The stored object has a server-generated key (`attachments/{applicationId}/{guid}.pdf|docx|png|jpg`) that never reaches the API.
- Deleting an attachment, an application or an offer (admin) and the demo data reset also delete the files (after the database commit; a failure to delete a file is only logged). Withdrawing keeps the attachments, and re-applying after a withdrawal keeps them too.
- The frontend checks type, size, count and total size before sending (together with the files the application already has, also when re-applying), sends all files in one request with a progress bar, and defaults the kind of a new file to CV until the application has one, then "Other". If the upload after applying fails or the user leaves the page, the application stays submitted and the message points to "My applications"; Escape in the upload dialog cancels the upload.

### Company photos and offer background

An employer can add a photo gallery to the company profile (at most 20 photos, each with an optional caption, in a chosen order) and a background image shown behind the header of the company's offers, with a dim level and a focal point.

| Endpoint | Who | Result |
| --- | --- | --- |
| `GET /api/companies/{id}/photos` | everyone for an approved company; admin and company members always | `[{ id, caption, width, height, url }]` in gallery order; `url` = `/api/companies/{id}/photos/{photoId}/file` |
| `GET /api/companies/{id}/photos/{photoId}/file` | as above | the image (`Content-Disposition: inline`, `X-Content-Type-Options: nosniff`, `Cache-Control: public, max-age=86400` for an approved company, `private, no-store` otherwise) |
| `GET /api/companies/{id}/offer-background/file` | as above | the background image (the same headers); no image - `404` `Offer background not found` |
| `POST /api/employer/company/photos` (multipart: one or more `files` fields) | employer | `201` with the whole gallery; new photos go to the end |
| `PATCH /api/employer/company/photos/{photoId}` `{ caption }` | employer | `200` with the photo; the caption is normalized like company names, at most 150 characters, empty - no caption |
| `PUT /api/employer/company/photos/order` `{ photoIds }` | employer | `200` with the gallery; the list must contain every photo of the company exactly once, otherwise `400` `Invalid photo order` |
| `DELETE /api/employer/company/photos/{photoId}` | employer | `204`; missing or another company's photo - `404` `Photo not found` |
| `PUT /api/employer/company/offer-background` (multipart: one `file` field) | employer | `200` `{ url, dim, focus }`; replaces the previous image (its file is deleted) |
| `PATCH /api/employer/company/offer-background` `{ dim, focus }` | employer | `200` `{ url, dim, focus }` (`url` is `null` before an image is uploaded); `dim` 55-95 (default 75), `focus` `0` center, `1` top, `2` bottom; an omitted field stays unchanged, an invalid value - `400` |
| `DELETE /api/employer/company/offer-background` | employer | `204` (also without an image); the dim level and the focal point are kept |
| `DELETE /api/admin/companies/{id}/photos/{photoId}`, `DELETE /api/admin/companies/{id}/offer-background` | admin | `204` (moderation) |

- Images: PNG or JPEG detected from the content, up to 5 MB per file. The width and height are read from the header (PNG `IHDR`, JPEG `SOFn` marker) without decoding the image and stored with the photo; a damaged header returns `400` `Unsupported image type - upload a PNG or JPEG file`, an image larger than 12,000 px per side or 40 megapixels - `400` `Image dimensions are too large - maximum is 12000 px per side and 40 megapixels` (a small file that would make the browser decode a huge bitmap).
- Uploads follow the attachment rules: the employer's company (and a full gallery) is checked before the body is read, `[RequestSizeLimit]` 26 MB with the `uploads` rate limit, all files validated before anything is stored, files written to storage before the records, and the 20-photo limit checked under a row lock on the company (concurrent uploads of two members never exceed it; the losing request's files are deleted). Other errors: `A company can have at most 20 photos`, `File is too large - maximum size is 5 MB`, `File is empty`, `No file uploaded - send images in the 'files' form field` / `... the image in the 'file' form field`, `Request is too large - upload at most 25 MB at once`, `Invalid multipart form data`.
- Storage keys are generated by the server (`companies/{companyId}/photos/{guid}.png|jpg`, `companies/{companyId}/offer-background/{guid}.png|jpg`) and never reach the API. Deleting a photo or the background, replacing the background, deleting a company (admin, or the account of its last member) and the demo data reset delete the files after the database commit.
- `CompanyDto` has `photosCount` and `offerBackground: { url, dim, focus } | null`; every `JobOfferDto` has `companyBackground` with the same shape (`null` when the company has no image or is not approved). Both are filled from the company loaded with the offer or the company list query - no extra query per item. The background `url` contains a version (`?v=` - the image's GUID), so a new image gets a new address and the day-long public cache never shows the old one.
- Images of a company that is not approved are visible only to its members and admins - the browser does not send the JWT with `<img src>`, so the frontend loads them with an authenticated request (blob URL) in that case.

### User account and e-mails

Every signed-in user has a "Moje konto" page (`/account`): the profile (first and last name, phone, headline, location, LinkedIn, about), password change, account deletion (confirmed with the password) and notification settings; candidates also get profile documents (e.g. a default CV) and "Zapisane oferty" (`/saved-jobs`, heart buttons on the list and on the offer details).

| Endpoint | Who | Result |
| --- | --- | --- |
| `GET/PUT /api/account/profile` | signed in | the profile (`email`, `emailConfirmed`, `roles`, `createdAt` and the profile fields); the e-mail address cannot be changed |
| `POST /api/account/change-password` `{ currentPassword, newPassword }` | signed in | `200` with a new token (old tokens stop working) |
| `DELETE /api/account` `{ password }` | candidate, employer | `204`; deletes the account with its applications, attachments, documents, saved offers and files; an admin and the last member of a company with offers get `400` |
| `GET/PUT /api/account/notifications` | signed in | `{ applicationStatusChanged, newApplication, companyReview }` (all `true` by default) |
| `GET/POST /api/account/documents`, `GET/DELETE /api/account/documents/{id}` | candidate | profile documents: uploaded like application attachments (the same types, 5 MB per file), at most 10 documents and 25 MB per profile; `POST` returns `201` with the full list |
| `PUT/DELETE /api/saved-jobs/{jobId}`, `GET /api/saved-jobs`, `GET /api/saved-jobs/ids` | candidate | saved offers (idempotent `204`; the list is paged, newest first, includes expired and closed offers, with category and skills) |
| `POST /api/auth/confirm-email` `{ userId, token }` | anyone | `204`; an invalid or expired link - `400` |
| `POST /api/auth/resend-confirmation` | signed in | `204`; an already confirmed address - `400` |
| `POST /api/auth/forgot-password` `{ email }` | anyone | always `202` (does not reveal whether the account exists) |
| `POST /api/auth/reset-password` `{ email, token, newPassword }` | anyone | `204`; an invalid or expired link - `400`, a weak password - `400` with validation errors |

- **Address confirmation**: registration sends a `<frontend>/confirm-email?userId=…&token=…` link (valid for 3 days, can be resent from the banner). An unconfirmed address blocks nothing - the frontend only shows a banner (`emailConfirmed` in the login and registration responses).
- **Password reset**: the `<frontend>/reset-password?email=…&token=…` link is valid for 1 hour and works once. A reset, a password change and an account deletion change the security stamp, so JWT tokens issued earlier get `401`. A reset also confirms the e-mail address and lifts the account lockout. The tokens in the links are Base64Url-encoded.
- **Notifications** (according to the settings): the candidate gets a confirmation of a submitted application and its status changes (with the recruiter's comment), the company's employers a new application (without the candidate's data) and the admin's decision about the company. Notifications go only to confirmed addresses (an unconfirmed address may belong to someone else); security e-mails (confirmation, password reset) are always sent.
- A wrong password when changing the password or deleting the account counts towards the account lockout like a failed login (`423` after 5 attempts).

E-mails are not sent during the request: each one is stored in `EmailMessages` in the same transaction as the event and sent in the background by `EmailOutboxProcessor` over SMTP (MailKit). A failed attempt is retried with a growing delay (30 s, 60 s, … up to 1 h; at most 8 attempts; a permanent rejection stops the retries at once), and the status, the number of attempts and the last error are kept. After sending (or the final failure) the body is cleared, and entries older than 30 days are deleted. In the Docker stack all e-mails go to Mailpit - nothing is sent outside.

The Data Protection keys that sign the tokens in the links are stored in the database (`DataProtectionKeys`), so the links survive a rebuild of the backend container. The keys are not encrypted (hence the `No XML encryptor configured` warning at startup).

### Statistics

Admins and employers get a "Statystyki" tab (lazy-loaded) with KPI tiles, a daily time series, a recruitment funnel, a table of offers, the median first-response time and a CSV export. Admins see the whole platform, an employer only the offers of their own company (the company comes from the database, never from the request).

| Endpoint | Who | Result |
| --- | --- | --- |
| `GET /api/admin/stats?from=&to=` | admin | `range`, `totals` (users by role, companies by status, offers by state, applications by status), daily `series` (`newApplications`, `newJobs`, `newUsers`, `jobViews`), `funnel`, `topJobs` (top 10 of the range), `medianFirstResponseHours` |
| `GET /api/employer/stats?from=&to=` | employer | `range`, `totals` (offers by state, applications by status, all-time `views`), `series` (`newApplications`, `jobViews`), `funnel`, `jobs` (all company offers with `views`, `applications`, `conversionRate`), `medianFirstResponseHours` |
| `GET /api/admin/stats/export`, `GET /api/employer/stats/export` | admin / employer | the daily series as CSV (UTF-8 with BOM, `;` separator, Polish header, `Content-Disposition: attachment; filename*=UTF-8''statystyki-YYYY-MM-DD_YYYY-MM-DD.csv`); cells starting with `=`, `+`, `-`, `@`, tab or CR are prefixed with `'` (CSV injection) |

- `from` and `to` are inclusive UTC days (`YYYY-MM-DD`); the default is the last 30 days ending today. `from > to` or a malformed day returns `400` `Invalid date range`, a range longer than 366 days `400` `Date range cannot exceed 366 days`. An employer without a company gets `403` `Employer account is not linked to a company`.
- `totals` are current / all-time values; `series`, `funnel`, `topJobs`, the employer `jobs` table and the median describe the requested range. The series contains every day of the range (missing days are filled with zeros on the server).
- Funnel: applications submitted in the range that reached a stage according to the status history. Stages are cumulative (`inReview` counts every application whose history contains InReview, Interview, Offered or Hired, and so on); rejection and withdrawal are not stages.
- Median first response: hours from submitting an application (in the range) to the first status change made by someone other than the candidate (a note without a status change does not count); `null` when there is none. The differences are computed in SQL and only the one or two middle values are read.
- All aggregations run in SQL (`GROUP BY` on the day, conditional counts); date filters use indexes on `JobApplications.AppliedAt`, `JobOffers.CreatedAt` and `AspNetUsers.CreatedAt`.
- Offer views: `GET /api/jobs/{id}` counts one view per (offer, visitor, UTC day) unless the caller is an admin or a member of the offer's company. The visitor is the user id, or the client IP plus User-Agent for anonymous callers; only an HMAC-SHA256 of it (16 bytes, key derived with HKDF from `JwtSettings:Secret`) is stored - never the IP. Counting is two statements in one transaction (`INSERT IGNORE` into `JobOfferViewers`, then `INSERT ... ON DUPLICATE KEY UPDATE` of the daily counter in `JobOfferDailyViews`); a failure is only logged and never breaks the offer response. A background service removes deduplication rows older than yesterday (hourly); the daily aggregates stay.
- Behind nginx the client IP is the same address the per-IP rate limits use (the last `X-Forwarded-For` entry - see "Security").

### Errors and request limits

An unhandled server error (e.g. a storage failure) returns `500` `{"message":"An unexpected error occurred"}` without details - the exception goes to the log only. A client error while reading the request (`BadHttpRequestException`) gets its own status and a generic message (`413` `Request body too large`, `408` `Request timed out`, others `400` `Invalid request`) and is logged as a warning.

Request bodies are limited to 1 MB (`RequestBodyLimitMiddleware` and Kestrel's global `MaxRequestBodySize`); upload endpoints raise the limit with `[RequestSizeLimit]` (26 MB). The limit also works independently of the server (a length-limited stream), e.g. on `TestServer` and for requests without `Content-Length`.

## Security

The project runs locally only, but the backend and nginx are hardened as if the application were exposed to a network. This section lists what is protected and the deliberate trade-offs that follow from the local scope.

### What is protected

- **Login and registration** - the whole `AuthController` uses the `auth` rate limiting policy (new authentication endpoints inherit it automatically): at most 10 requests per minute per client IP and 4 at once overall (no queue - password hashing is CPU-heavy). Exceeding a limit returns `429` `Too many requests - try again later` with `Retry-After` (seconds until the end of the window, `1` for the concurrency limit).
- **Security e-mails** - `forgot-password` (per client IP) and `resend-confirmation` (per user) use only the `account-emails` policy: 5 requests per 15 minutes, plus at most 3 password reset e-mails per account per hour. Their own `[EnableRateLimiting]` replaces the `auth` policy (ASP.NET Core applies the attribute closest to the endpoint, so the limits do not add up).
- **Uploads** - 30 requests per 10 minutes per user (a fixed window from the first request) and 8 uploads at once for all users (no queue); a rejection by the concurrency limit does not use up the window. Profile documents and `from-profile` count towards the same limit.
- **Public lists** (`GET /api/jobs`, `GET /api/jobs/facets`, `GET /api/companies`, `GET /api/categories`, `GET /api/skills`, including search): one shared limit of 120 requests per minute per client IP. Offer and company details are not limited. Statistics endpoints require a signed-in admin or employer and have no IP limit.
- **Client IP behind nginx** - the per-IP limits and the offer view counter see the real address: the backend reads `X-Forwarded-For` (`UseForwardedHeaders`, last entry only, i.e. the one appended by nginx, so spoofed earlier entries do not help).
- **Account lockout** after 5 failed attempts for 5 minutes, without account enumeration: a wrong password, an unknown account and a wrong password for a locked account always return `401` `Invalid email or password` in comparable time (an unknown account is verified against a dummy hash). Only the **correct** password of a locked account gets `423 Locked`. Attempts during the lockout neither change the counter nor extend the lockout. Parallel attempts for one account are serialized (an in-process lock per normalized e-mail, removed after use), so the counter never loses an attempt.
- **Input validation** - e-mail up to 256 characters and password up to 100 in every DTO with credentials (`CredentialLimits`, the same limit as in the frontend forms), search terms and location filters up to 100 characters, list filters validated on the server (`400`), page numbers capped at 100,000.
- **JWT** - HS256 only, 30 s clock skew, 120-minute lifetime, no refresh. The token carries a hash of the user's security stamp (the first 16 bytes of its SHA-256 - the payload is readable) and every request checks that the account still exists and the stamp is current, so a token of a deleted account (e.g. after a demo data reset) or of an account whose password changed gets `401`.
- **Authorization of every resource** through `ICurrentUserAccess`; another company's resources return `404`.
- **Uploads** - type detected from the content, size and count limits, ZIP structure checks for DOCX (no extraction, zip-bomb limits), server-generated storage keys (no path traversal), sanitized download names.
- **Headers** - nginx (frontend and proxied API): `Content-Security-Policy`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy` (no camera, microphone, geolocation, payment, USB), `Cross-Origin-Opener-Policy: same-origin`, no nginx version (`server_tokens off`); gzip; hashed build files (`*.js`, `*.css`, `*.woff2`) get `Cache-Control: public, max-age=31536000, immutable`, `index.html` gets `no-cache`. The headers are set only at `server` level (`add_header` in a `location` block would drop the inherited ones - guarded by `NginxConfigTests`). Backend: no `Server` header, `X-Content-Type-Options: nosniff` on every API response, `Cache-Control: no-store` on responses with user data.
- **CSP** - `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src 'self' https://fonts.gstatic.com; img-src 'self' data: blob:; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'`. Scripts come only from the own origin with no exceptions: the production build does not inline critical CSS (`optimization.styles.inlineCritical: false`), because that feature adds an inline script to `index.html`. The alternatives - the hash of that script in the header (changes with the Angular version) and `security.autoCsp` (a `<meta>` CSP whose hash changes with every build, forcing `'unsafe-inline'` in the static header) - were rejected. The cost is a render-blocking stylesheet of about 6 kB compressed, negligible for an SPA that renders after `main.js` anyway. `'unsafe-inline'` in `style-src` is needed because Angular and Angular Material inject component styles.
- **Swagger** only in Development or with `Swagger:Enabled=true`. The backend does not use `UseHttpsRedirection` (it serves HTTP behind nginx).
- **Containers** - the backend runs as the non-root `app` user of the .NET image, every service runs with `no-new-privileges`, ports are published on `127.0.0.1` only, secrets are random and kept in `.env` (outside the repository), SeaweedFS rejects unsigned requests.
- **Logs** without SQL statements (`Microsoft.EntityFrameworkCore.Database.Command` at `Warning`), server errors without details in responses, no raw IP addresses stored for view statistics.

### Deliberate trade-offs

- **HTTP without TLS and HSTS** - the stack listens on `127.0.0.1` only; certificates and HSTS make no sense locally.
- **Rate limits and login locks live in process memory** - one backend instance; a restart resets the rate limit counters (the account lockout is stored in the database and survives it). Several instances would each count separately, and the e-mail outbox assumes a single sender.
- **`X-Forwarded-For` is accepted from any sender** (`KnownNetworks`/`KnownProxies` are cleared because the nginx address in the Docker network is not fixed). The backend is reachable only through nginx and directly on the host's `127.0.0.1` - a local process calling the backend past nginx can send any address and bypass the per-IP limits or inflate the view counter.
- **View statistics are a popularity metric, not an audit log** - anonymous visitors that share an IP and a User-Agent count as one visitor per day, and a client that varies its User-Agent can inflate the counter.
- **`423` with the correct password** reveals the lockout to whoever knows the password. Account enumeration is gone, but during the lockout the response still tells a correct password from a wrong one, so guessing the password of a locked account is limited by the per-IP limit (10 attempts per minute), not by the lockout itself.
- **No `limit_req` in nginx** - the backend enforces the per-IP limits with a uniform JSON `429` and `Retry-After`; a second limit in nginx would answer with an HTML page without `Retry-After`.
- **Self-hosted fonts** - Roboto (400/500/700, Latin and Latin Extended subsets) and Material Icons are served from the app's own origin (`frontend/public/fonts`, WOFF2; `font-display: swap` for Roboto, `block` for the icon font so ligature names never flash as text), and only the two most common Roboto weights are preloaded. The icon font is the complete set (about 125 kB, cached by the browser) - subsetting it would need a build step outside the Angular CLI. The frontend no longer needs the `fonts.googleapis.com` and `fonts.gstatic.com` sources in the CSP.
- **`ALLOW_SEED_REFRESH=true` by default in Docker** - a convenient demo reset. Whoever takes over an admin account (e.g. the old `Admin123!` password in a database created before `ADMIN_PASSWORD`, a stolen token, an XSS flaw) can delete all users, companies, offers, applications and files with a single `POST /api/admin/refresh-seed`, after which the demo accounts with public passwords exist again. With your own data set `ALLOW_SEED_REFRESH=false` and `SEED_DEMO_ACCOUNTS=false`.
- **MySQL as `root`** - a separate application user (`MYSQL_USER`) is created only when an empty volume is initialized, and existing `db-data` volumes do not have one; introducing it would need a manual data migration. The database port is published on `127.0.0.1` only and `DB_PASSWORD` sets the root password.
- **Demo accounts and old admin passwords** - the demo accounts have publicly known passwords (turned off with `SEED_DEMO_ACCOUNTS=false`), and admin accounts created before `ADMIN_PASSWORD` keep `Admin123!` until a demo data reset or removal of the volume.
- **Swagger enabled by default in Docker** - the API documentation is part of the portfolio (`SWAGGER_ENABLED=false` turns it off).
- **Unencrypted Data Protection keys** in the database - acceptable for a local stack.
- **No token refresh** - after 120 minutes the user signs in again.

## Running tests

### Backend

Backend tests (xUnit) live in `Backend.Tests` (part of `Backend/Backend.sln`): unit tests (`Unit/`) and integration tests (`Integration/`) that start the application through `WebApplicationFactory` against a real MySQL server.

```bash
cd Backend.Tests
# fast suite - without the migration tests (each creates and migrates its own databases)
dotnet test --filter "Category!=Migrations"
# full suite
dotnet test
# migration tests only
dotnet test --filter "Category=Migrations"
```

Migration test classes carry `[Trait("Category", "Migrations")]` (checked by `TestInfrastructureTests`) - every new `*MigrationTests` class needs it too.

Each test factory creates its own database named `JobBoardTest_<guid>` (migrations and seed run at startup) and drops it afterwards - `JobBoardDb` is never touched. The server and credentials come from these environment variables:

| Variable | Meaning |
| --- | --- |
| `JOBBOARD_TEST_CONNECTION` | a MySQL connection string; the database name is ignored and replaced. Without it `ConnectionStrings__DefaultConnection` is used (server and credentials only), and without both `Server=localhost;Port=3306;User=root;Password=devpass` |
| `JOBBOARD_TEST_S3_URL` | an S3-compatible endpoint (e.g. `http://localhost:8333`) for the `S3FileStorage` tests; they are skipped without it. Each test creates and deletes its own bucket |
| `JOBBOARD_TEST_S3_ACCESS_KEY`, `JOBBOARD_TEST_S3_SECRET_KEY` | optional keys for the S3 tests |
| `JOBBOARD_TEST_MAILPIT_URL` | the Mailpit API address (e.g. `http://localhost:8025`, SMTP on port 1025 of the same host) for the real SMTP test (`MailpitSmtpTests`); skipped without it |

Example: `JOBBOARD_TEST_CONNECTION="Server=localhost;Port=3306;User=root;Password=secret;AllowPublicKeyRetrieval=true;" dotnet test --filter "Category!=Migrations"`.

Integration tests store files in the `Local` storage, in a temporary directory per factory (deleted with its database). The test factory replaces SMTP with a fake (`FakeEmailSender` - tests read the sent e-mails, decode the links and use them against the API) and raises the upload, login, public list and security e-mail limits (all test requests share one client address); the tests of these limits use the defaults and pass client addresses in `X-Forwarded-For` where they need separate ones. Outside Development the factory sets `Seed:AdminPassword` to the Development admin password.

### Frontend

```bash
cd frontend
npm ci
npx ng test --watch=false   # unit tests (Vitest)
npx ng build                # production build
```

## Repository layout

```
.
├── Backend/                  ASP.NET Core 9 API
│   ├── Controllers/          REST endpoints (jobs, companies, applications, account, stats, admin)
│   ├── Data/                 ApplicationDbContext and the demo data seeder
│   ├── DTOs/                 request and response contracts
│   ├── Helpers/              paging, query parameters, filters, limits
│   ├── Interfaces/           repository and service contracts
│   ├── Mappings/             entity <-> DTO mapping
│   ├── Migrations/           EF Core migrations (MySQL)
│   ├── Models/               entities
│   ├── Repositories/         data access (LINQ only)
│   ├── Services/             auth, storage, e-mail outbox, rate limiting, statistics, middleware
│   └── Settings/             validated options classes
├── Backend.Tests/            xUnit tests
│   ├── Infrastructure/       test factory, fixtures, API helpers
│   ├── Integration/          API and migration tests on MySQL
│   └── Unit/                 unit tests
├── frontend/                 Angular 22 application
│   ├── docker/               nginx configuration
│   └── src/app/
│       ├── components/       pages, panels, dialogs, shared components, statistics charts
│       ├── guards/           route guards
│       ├── interceptors/     attaches the JWT, handles expired sessions
│       ├── models/           API types
│       ├── services/         API clients
│       └── utils/            labels, query mapping, error messages
├── docs/                     notes for continuing work on the feature stages
├── docker-compose.yml        the local stack (MySQL, SeaweedFS, Mailpit, backend, frontend)
├── docker-up.ps1             start script: free ports, secrets, build and start
└── .env.example              template of the stack settings
```

## Known limitations

- **Local only** - no TLS, no deployment scripts, no CI; the Docker stack is meant for one machine.
- **One backend instance** - rate limits and login serialization live in process memory, and the e-mail outbox and the view cleanup run in the backend process; several instances would need a shared store.
- **Salaries are compared in the offer's own currency** - there is no currency conversion, so a salary filter makes sense together with the currency filter.
- **View counts are approximate** - see the trade-offs above.
- **Out-of-order migrations** - `AddJobViewStatistics` (stage 6) is dated a few minutes before `AddJobTaxonomy` (stage 5) because both stages were developed in parallel. EF Core applies whichever is missing, so updating any existing database works, but rolling back to a migration between the two also rolls back the other.
- **Abandoned `AddApplicationCv` migration** - an earlier "one CV per application" model never reached `main` and was replaced by `AddApplicationAttachments`. Development databases where it was applied have empty `Cv*` columns in `JobApplications` and an entry in `__EFMigrationsHistory` (harmless, can be removed by hand).
- **Requests without `Content-Length`** are rejected only after 26 MB have been received; Kestrel then drops the connection and the browser may not get the JSON error, which is why the frontend validates file sizes first.
- **The initial bundle exceeds the 500 kB budget** (about 577 kB raw / 142 kB compressed, a build warning) - the screens are lazy-loaded and the Material theme emits only system tokens, but the shell still pulls in Angular core, the router and the CDK overlay (the snack bar used by the auth service).
- **.NET 9, not .NET 10** - waits for a stable Pomelo provider for EF Core 10.
- **Polish-only interface** - the UI is in Polish; API messages are in English and translated in the frontend.

## Third-party fonts

The fonts are bundled in `frontend/public/fonts` together with their licenses:

- **Roboto** (from `@fontsource/roboto` 5.3.0) - SIL Open Font License 1.1, `LICENSE-Roboto-OFL.txt`.
- **Material Icons** (from `material-icons` 1.13.14) - Apache License 2.0, `LICENSE-MaterialIcons-Apache.txt`.

## Screenshots

Screenshots will be added after the upcoming interface refresh.
