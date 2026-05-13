# AI CapEx Slowdown Monitor

Developer-friendly MVP dashboard for tracking public indicators of AI infrastructure capex momentum and producing a 0-100 AI CapEx Slowdown Risk Score.

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

Signals are normalized from `-100` bullish to `+100` bearish, mapped to a 0-100 risk scale, and weighted into the final score.

## Seeded Data

The app seeds MSFT, AMZN, GOOGL, META, ORCL, NVDA, AMD, MU, SNDK, ASML, TSM, AVGO, ANET, and VRT with sample financial metrics, transcript mentions, source documents, indicator signals, score history, and alerts.

## TODOs For Real Data

- Connect SEC EDGAR XBRL companyfacts/submissions ingestion.
- Connect earnings-call transcript providers.
- Add RSS/news ingestion with source reliability scoring.
- Add PostgreSQL provider and migrations.
- Add scheduled ingestion jobs.
- Add authentication and manual-entry roles.
