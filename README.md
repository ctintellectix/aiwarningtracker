# AI CapEx Slowdown Monitor

Developer-friendly MVP dashboard for tracking public indicators of AI infrastructure capex momentum and producing a 0-100 AI CapEx Expansion Score.

## Stack

- ASP.NET Core Web API
- EF Core with SQLite
- React + TypeScript + Vite
- xUnit tests for scoring and transcript analysis

Note: this workstation has the .NET 10 SDK installed, but not the .NET 8 targeting pack. The project is therefore scaffolded as `net10.0` so it runs here. To retarget to .NET 8 after installing the .NET 8 SDK, change each `.csproj` `TargetFramework` from `net10.0` to `net8.0` and use EF/OpenAPI package versions compatible with .NET 8.

## Run Locally

Backend:

```powershell
dotnet restore AiCapexSlowdownMonitor.slnx
dotnet run --project src/AiCapex.Api/AiCapex.Api.csproj --launch-profile http
```

Frontend:

```powershell
cd frontend
npm install
npm run dev
```

Open `http://localhost:5173`. The API runs at `http://localhost:5087`.

## Real Data Configuration

The app remains usable without demo data when external API keys are missing by using cached real imports, RSS/source documents, and manual analyst indicator inputs.

SEC EDGAR ingestion uses the official companyfacts API. SEC requires a descriptive User-Agent, so set one in `src/AiCapex.Api/appsettings.Development.json` or as an environment variable:

```powershell
$env:SEC_USER_AGENT="AiCapexSlowdownMonitor/1.0 (you@example.com)"
```

Optional OpenAI narrative analysis can replace keyword-only RSS/transcript scoring with structured document summaries and category signals:

```powershell
$env:OPENAI_API_KEY="your-key"
$env:OpenAI__Enabled="true"
```

When enabled, imported RSS articles and transcripts are analyzed once during ingestion and the structured results are stored with the source document for attribution. If the key is missing or the API call fails, the app falls back to the existing keyword analyzer so imports still complete.

EarningsCallBiz public transcript lookup is enabled by default and uses cached, respectful requests:

```json
"TranscriptProviders": {
  "EarningsCallBiz": {
    "Enabled": true,
    "BaseUrl": "https://earningscall.biz",
    "UserAgent": "AiCapexMonitor/1.0 (contact@example.com)",
    "CacheDays": 7,
    "RequestDelayMs": 1000
  }
}
```

## Manual Imports

With the API running, trigger SEC imports manually:

```powershell
Invoke-RestMethod -Method Post http://localhost:5087/api/import/sec/MSFT
```

Trigger RSS/news imports:

```powershell
Invoke-RestMethod -Method Post http://localhost:5087/api/import/rss
```

Run every real-data import for all tracked companies:

```powershell
Invoke-RestMethod -Method Post http://localhost:5087/api/import/all
```

This runs SEC imports, transcript provider imports for the latest four quarters per tracked ticker, RSS/news imports, then recalculates the risk score.

Clear imported data while preserving tracked companies:

```powershell
dotnet run --project tools/AiCapex.DbTool/AiCapex.DbTool.csproj -- clear-imports src/AiCapex.Api/ai-capex-monitor.db
```

Transcript provider lookup:

```powershell
Invoke-RestMethod http://localhost:5087/api/transcripts/NVDA/2026/1
Invoke-RestMethod http://localhost:5087/api/transcripts/earningscallbiz/nasdaq/hood/2026/1
```

Useful real-data endpoints:

- `GET /api/settings/data-sources`
- `POST /api/import/sec/{ticker}`
- `POST /api/import/all`
- `POST /api/import/rss`
- `POST /api/alerts/generate`
- `GET /api/transcripts/{ticker}/{year}/{quarter}`
- `GET /api/transcripts/earningscallbiz/{market}/{ticker}/{year}/{quarter}`
- `GET /api/companies/{ticker}/financials`
- `GET /api/sources/documents`
- `GET /api/risk/latest`
- `GET /api/risk/history`
- `GET /api/risk/latest/attribution`

## Test

```powershell
dotnet test AiCapexSlowdownMonitor.slnx
cd frontend
npm run build
```

## Docker

```powershell
docker compose up --build
```

## Risk Model

The score is configured in `src/AiCapex.Api/appsettings.json`:

- 30% hyperscaler capex revision trend
- 20% HBM / DRAM pricing and allocation signal
- 15% CoWoS / advanced packaging signal
- 15% data center leasing / power signal
- 10% AI revenue monetization signal
- 10% financial stress / free cash flow signal

All signal impacts use one shared `-10` to `+10` momentum scale across OpenAI analysis, manual entries, derived signals, company pages, alerts, and dashboard rollups. The final expansion score converts those category signals into the separate `0` to `100` dashboard score without any hidden multiplier. `100` on the overall expansion score means the strongest expansion momentum; `0` means the weakest. The public-facing bands are intentionally plain-language: `0-19 very weak`, `20-39 weak`, `40-59 neutral`, `60-79 strong`, and `80-100 very strong`.

## Tracked And Real Data

The app maintains the tracked company list for MSFT, AMZN, GOOGL, META, ORCL, NVDA, AMD, MU, SNDK, ASML, TSM, AVGO, ANET, VRT, SMCI, DELL, and MRVL. By default, `SeedData:UseSampleData` is `false`, so the app does not inject demo financial metrics, transcript mentions, source documents, indicator signals, score history, or alerts. When sample data is disabled, startup also purges known demo rows from the local SQLite database while preserving real SEC imports, RSS imports, manual entries, and provider transcripts.

To enable demo observations for local UI testing only, set:

```json
"SeedData": {
  "UseSampleData": true,
  "PurgeSampleDataWhenDisabled": false
}
```

SEC imports currently normalize likely capex, operating cash flow, revenue, and debt facts from EDGAR companyfacts JSON across both `us-gaap` and `ifrs-full` taxonomies when those facts are present. Imported facts keep source URLs and source-document records, then feed the company financial charts.

Transcript ingestion uses cached/local transcripts first, then EarningsCallBiz public pages. Manual transcript upload, company IR URL discovery, public web transcript discovery, and the older paid transcript providers have been removed. The all-import workflow attempts transcript imports for the latest four quarters for every tracked ticker.

EarningsCallBiz URLs use `https://earningscall.biz/e/{market}/s/{ticker}/y/{year}/q/q{quarter}`. The `market` segment must match the source site, with common values such as `nasdaq` and `nyse`. Imported transcripts store the source URL for attribution, use an app User-Agent, cache successful results for seven days, cache not-found results for twelve hours, and fall back to cached data when rate-limited. For commercial use, review earningscall.biz terms or use an official/licensed API.

RSS/news imports read `NewsFeeds` from `src/AiCapex.Api/appsettings.json`, store deduped source documents by URL, and send each new article through OpenAI narrative analysis to create indicator signals.

Dashboard scoring is period-aware. SEC metrics preserve company-specific fiscal period end dates, and score calculations use the latest available company records up to the current calendar quarter end rather than letting one provider's future fiscal label define the whole dashboard. Transcript records keep their source fiscal label plus call/period date where available; provider labels should still be reviewed for edge cases because transcript sites may not always align perfectly to issuer fiscal calendars.

Watchlist alerts are generated after imports, manual entries, transcript imports, explicit scoring runs, and app startup recalculation. The current rule set covers configurable expansion-score deterioration, configurable capex/OCF stress, and configurable weakening thresholds for HBM/DRAM, CoWoS/packaging, or data center/power signals. Alerts are deduped by title and message.

## Limitations And Disclaimer

SEC XBRL tags vary by issuer and foreign issuers may not expose the same companyfacts data as U.S. filers. RSS feeds can change formats or rate-limit requests, so imports are best treated as source-monitoring inputs rather than a complete news dataset.

## TODOs For Real Data

- Add scheduled transcript and RSS import jobs.
- Add full Whisper/faster-whisper audio transcription.
- Add PostgreSQL provider and migrations.
- Add authentication and manual-entry roles.

This project is for monitoring and research workflows only. It is not investment advice.
