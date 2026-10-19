# ExpensesBot

[![CI](https://github.com/runolight/ExpensesBot/actions/workflows/ci.yml/badge.svg)](https://github.com/runolight/ExpensesBot/actions/workflows/ci.yml)

A personal finance tracker that lives inside Telegram: a chat bot for quick
entries and a Blazor WebAssembly **Telegram Mini App** for everything else.
Built with .NET 10, ASP.NET Core Minimal APIs and EF Core.

> The user interface and bot messages are in Russian; the code and docs are in English.

## What it does

- **Record in two taps** — send `1500` to the bot, choose *Expense* or *Income*, done.
- **Mini App dashboard** — income, expenses and balance converted into one display currency; spending by category and by month; search, filter, sort.
- **Multi-currency** — AMD, USD and RUB with daily rates from the Central Bank of Russia.
- **Budgets** per category or overall, with a Telegram alert at 90%.
- **Savings goals** with contributions and progress.
- **Recurring transactions** (daily / weekly / monthly / yearly), income or expense, executed automatically.
- **Tags**, **CSV export and import** (the export can be re-imported).
- **Notifications** — evening reminder, weekly report, budget alerts; `/report` in chat.

## Frontend

The Blazor client is plain Razor components and two stylesheets: no CSS framework, no SCSS, no build step.

| File | Purpose |
|---|---|
| `wwwroot/css/tokens.css` | Design tokens: colours, typography, radii, shadows. One `.expenses-app.dark` block overrides them for the dark theme. |
| `wwwroot/css/app.css` | Base styles and component styles (BEM: `card`, `card__header`, `btn--primary`), plus Blazor's loading and error UI. |
| `wwwroot/js/telegram.js` | Telegram Mini App bridge (`initData`, start parameter, theme, native confirm dialogs) and CSV file helpers. |

`Pages/Home.razor` composes the page and owns the data; each panel in `Components/` owns its own form and open/closed state and reports back through callbacks.

| Component | Purpose |
|---|---|
| `AppHeader` | Title, display currency, help, theme, import and account actions |
| `HelpModal` | In-app guide: bot notifications, bot commands, currencies, recurring rules, budgets, goals |
| `Modal` | Overlay + card with title, close button and optional action buttons |
| `PanelHeader` | Panel title with an add/close toggle and optional extra buttons |
| `TypeToggle`, `CurrencySelect`, `CategorySelect` | Form inputs shared by every form (`@bind-Value`) |
| `ProgressCard`, `ProgressBar` | Budget and goal cards with a coloured progress bar |
| `BarRow`, `PieChart` | Breakdown charts (hand-rolled SVG, culture-invariant) |
| `TagSelector`, `TagChip` | Tag picking and creation in the transaction form; read-only tag labels in history |
| `TransactionList`, `SearchFilterBar` | History with client-side search, category and tag filters, and sort |
| `EmptyState`, `ErrorBanner` | Placeholders and dismissible errors |

Conventions: colours come from tokens, never inline styles except data-driven values (bar widths, tag colours via `--tag-color`); confirmations go through `IJSRuntime.ConfirmAsync`, which uses Telegram's native dialog inside the Mini App.

## Architecture

```mermaid
flowchart LR
    U([Telegram user]) -- chat --> TG[Telegram Bot API]
    U -- opens Mini App --> C[ExpensesBot.Client<br/>Blazor WASM on GitHub Pages]
    C -- "HTTPS + Authorization: tma &lt;initData&gt;" --> A[ExpensesBot.Api<br/>Minimal API]
    W[ExpensesBot.Worker<br/>Hosted service] -- long polling / sendMessage --> TG
    W -- daily rates --> CBR[(cbr-xml-daily.ru)]
    A --> DB[(SQLite)]
    W --> DB
```

| Project | Responsibility |
|---|---|
| `ExpensesBot.Api` | REST API. `Authentication/` — ASP.NET Core authentication handler for Telegram `initData`; `Application/` — one service per feature (transactions, summary, budgets, goals, …) returning `Result<T>`; `Endpoints/` — thin Minimal API mappings; `Contracts/` — request/response records. Applies EF Core migrations on startup. |
| `ExpensesBot.Client` | Blazor WebAssembly Mini App, deployed to GitHub Pages. |
| `ExpensesBot.Data` | EF Core model, migrations, and shared rules: supported currencies, new-user defaults, database path resolution. |
| `ExpensesBot.Worker` | Telegram bot (long polling), recurring transactions, notifications, exchange-rate updates. |
| `*.Tests` | xUnit tests: API integration tests on in-memory SQLite, bUnit component tests, Worker and Data unit tests. |

## Design decisions

- **Money is stored as `long` minor units** (cents/luma) together with an ISO currency code. No floating-point rounding errors; conversions round once, explicitly.
- **Authentication without passwords.** The Mini App sends Telegram's signed `initData`; the API checks the HMAC-SHA256 signature with the bot token ([Telegram docs](https://core.telegram.org/bots/webapps#validating-data-received-via-the-mini-app)), rejects stale data, and creates the user on first visit. See `TelegramInitDataValidator`.
- **Standard ASP.NET Core auth pipeline.** `TelegramAuthenticationHandler` turns a valid `initData` into a `ClaimsPrincipal` (creating the user on first visit); route groups use `.RequireAuthorization()` and endpoints just read `user.GetUserId()`.
- **Thin endpoints, testable services.** Business rules live in application services that return `Result<T>` instead of throwing; endpoints map results to HTTP, and errors are returned as RFC 9457 problem details. Services are unit-tested directly against SQLite, without HTTP.
- **Development login is Development-only.** For local work the API also accepts `X-Dev-User-Id`, but only when `ASPNETCORE_ENVIRONMENT=Development`, and Release builds of the client never send it. A test starts the API as Production and checks that the header is ignored.
- **Cross-currency totals are converted, never summed raw.** The summary endpoint converts every transaction into the requested display currency using the latest stored rates (with clearly flagged fallback rates if none are stored yet).
- **The bot is stateless.** One handler serves every chat, so the transaction type and amount travel inside the button's `callback_data` (`tx:i:1500`). The confirmation message is edited after recording, so a second tap can't create a duplicate.
- **Notifications are idempotent.** Every sent reminder, weekly report and budget alert is recorded in `NotificationLogs` with a unique `(UserId, Kind, Key)` index. The Worker checks it before sending, so restarts never resend a message, and only transient Telegram failures (network, 5xx, 429) are retried.
- **One SQLite file, two processes.** The API and the Worker share the database. Relative paths are resolved against the repository root so both open the same file, and both apply migrations at startup under EF Core's migration lock.

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A Telegram bot token from [@BotFather](https://t.me/BotFather) (only needed for the bot and for real Mini App logins)

### Configuration

| Setting | Used by | Default | Notes |
|---|---|---|---|
| `ConnectionStrings:ExpensesDatabase` | API, Worker | `Data Source=data/expenses.db` | Relative paths resolve against the repo root. |
| `Telegram:BotToken` (or env `TELEGRAM_BOT_TOKEN`) | API, Worker | — | Keep it out of git: use user-secrets or environment variables. |
| `Telegram:WebAppUrl` | Worker | GitHub Pages URL | URL the bot's "Open app" buttons point to. |
| `Telegram:DailyReminderHourUtc` | Worker | `17` | Hour (UTC) of the evening reminder. |
| `Telegram:InitDataMaxAgeHours` | API | `24` | How long a Mini App session stays valid. |
| `Cors:AllowedOrigins` | API | GitHub Pages origin | Add the origin the client is served from. |
| `ApiBaseUrl` (`Client/wwwroot/appsettings*.json`) | Client | `http://localhost:5069/` in Development | Public URL of the API for the deployed client. |

```bash
# API and Worker share one user-secrets store, so this configures both
# (alternatively export TELEGRAM_BOT_TOKEN)
dotnet user-secrets --project ExpensesBot.Worker set "Telegram:BotToken" "<token>"
```

### Run locally

```bash
dotnet run --project ExpensesBot.Api      # http://localhost:5069 (creates data/expenses.db)
dotnet run --project ExpensesBot.Worker   # bot + background jobs
dotnet run --project ExpensesBot.Client   # http://localhost:5160
```

Opened in a normal browser, the Development client signs in as a test user via `X-Dev-User-Id`.
`ExpensesBot.Api/ExpensesBot.Api.http` has ready-made requests.

### Run with Docker

```bash
cp .env.example .env        # set TELEGRAM_BOT_TOKEN (and CLIENT_ORIGIN / WEBAPP_URL if needed)
docker compose up -d --build
```

The API listens on `http://localhost:8080` and the Worker runs the bot; both use one SQLite database on the `expenses-data` volume. Both containers run in Production mode, so the development header is disabled.

### Use it from Telegram

1. Create a bot with @BotFather and set the token as above.
2. Expose the API over HTTPS (for example `cloudflared tunnel --url http://localhost:5069`) and put that URL in `Client/wwwroot/appsettings.json` → `ApiBaseUrl`.
3. **Run the API in Production** (`ASPNETCORE_ENVIRONMENT=Production`) whenever it is reachable from the internet.
4. Deploy the client (the `Deploy` workflow publishes it to GitHub Pages) and register that URL with @BotFather (`/newapp` or the bot's menu button).
5. Send `/start` to the bot.

Bot commands: `/start`, `/menu`, `/expenses` (open the app), `/report` (last 7 days), or just send an amount.

## Tests

```bash
dotnet test ExpensesBot.slnx
```

- **Application service tests** exercise summary math, budget and goal progress directly against a migrated in-memory SQLite database with a fixed clock.
- **API integration tests** run the real application (`WebApplicationFactory`) against an in-memory SQLite database created from the migrations. They cover validation, per-user isolation, currency conversion, summary math, goals, CSV export, and authentication with correctly signed, tampered, expired and foreign `initData`.
- **Component tests** (bUnit) cover the summary card, history search/filter/sort, the edit and delete-account dialogs, shared form inputs, goal contributions and the help dialog.
- **Worker and Data tests** cover recurrence scheduling, CBR rate parsing, bot input parsing, currency rules, and a check that the migrations match the EF model.

GitHub Actions runs build and tests on every push and pull request (`.github/workflows/ci.yml`); `deploy.yml` publishes the client to GitHub Pages.

## API overview

| Method | Route | Purpose |
|---|---|---|
| GET/POST/PUT/DELETE | `/api/transactions[/{id}]` | Transactions (paged list with optional `tagId` filter, create, update, delete) |
| POST | `/api/transactions/bulk` | Create up to 100 transactions, with per-row errors |
| GET | `/api/transactions/summary?mode=all\|30days\|month&currency=AMD` | Totals, breakdowns, average and projection |
| GET | `/api/transactions/export` | CSV export |
| GET/POST/PUT/DELETE | `/api/categories[/{id}]`, `POST /{id}/archive` | Categories |
| GET/POST/PUT/DELETE | `/api/budgets[/{id}]` | Budgets with spent amount and progress |
| GET/POST/PUT/DELETE | `/api/goals[/{id}]`, `POST /{id}/contribute` | Savings goals |
| GET/POST/PUT/DELETE | `/api/recurring[/{id}]`, `POST /{id}/execute` | Recurring rules |
| GET/POST/PUT/DELETE | `/api/tags[/{id}]` | Tags |
| GET/PUT/DELETE | `/api/profile`, `/api/profile/currency` | Profile, default currency, account deletion |
| GET/POST | `/api/exchange/rates`, `/api/exchange/convert` | Exchange rates |

In Development the OpenAPI document is served at `/openapi/v1.json`.

## Roadmap

- [x] Persist notification state so restarts never resend alerts
- [ ] Move to PostgreSQL for multi-instance deployments
- [x] Editing budgets, goals and tags
- [x] Tag filters

## License

[MIT](LICENSE)
