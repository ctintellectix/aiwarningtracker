# AI CapEx Slowdown Monitor Design

## Goal

Build a running full-stack MVP that tracks public indicators of AI infrastructure capex momentum and displays a configurable 0-100 AI CapEx Slowdown Risk Score.

## MVP Direction

Optimize first for a local developer demo with realistic seeded data, SQLite, and clear setup. Keep the architecture production-shaped so the app can grow soon into PostgreSQL, scheduled ingestion, authentication, and richer source integrations.

## Architecture

The project uses a clean architecture style:

- `AiCapex.Domain`: entities, enums, and core scoring models.
- `AiCapex.Application`: scoring, transcript analysis, alert generation, DTOs, and ingestion interfaces.
- `AiCapex.Infrastructure`: EF Core persistence, SQLite setup, seed data, repositories, and stub ingestion services.
- `AiCapex.Api`: ASP.NET Core Web API controllers and dependency registration.
- `frontend`: React + TypeScript dashboard.
- `tests/AiCapex.Application.Tests`: unit tests for scoring and transcript analysis.

SQLite is configured through infrastructure and accessed through EF Core. Future PostgreSQL migration should require a provider/package swap plus connection string configuration, not application logic changes.

## Data Model

The MVP includes these entities:

- `Company`
- `FiscalQuarter`
- `FinancialMetric`
- `Transcript`
- `TranscriptMention`
- `IndicatorSignal`
- `RiskScoreSnapshot`
- `SourceDocument`
- `WatchlistAlert`

Companies are seeded for MSFT, AMZN, GOOGL, META, ORCL, NVDA, AMD, MU, SNDK, ASML, TSM, AVGO, ANET, and VRT.

## Scoring Model

Weights are configurable in `appsettings.json`:

- Hyperscaler capex revision trend: 30
- HBM / DRAM pricing and allocation signal: 20
- CoWoS / advanced packaging signal: 15
- Data center leasing / power signal: 15
- AI revenue monetization signal: 10
- Financial stress / free cash flow signal: 10

Signals use a normalized direction where negative values are bullish and positive values are bearish. The weighted model maps category values into a bounded 0-100 risk score and assigns one risk band:

- 0-25: Bullish acceleration
- 26-45: Healthy expansion
- 46-60: Watch zone
- 61-75: Slowdown forming
- 76-100: Capex rollover risk

## Ingestion

The MVP ships with pluggable interfaces and stub implementations for:

- SEC EDGAR XBRL financial data
- Earnings call transcript text
- Manual indicator entry
- RSS/news-style source entries

Live source integration is intentionally deferred. Seeded data makes the app useful immediately and provides examples for future ingestors.

## API

The Web API exposes:

- `GET /api/dashboard/summary`
- `GET /api/companies`
- `GET /api/companies/{ticker}`
- `GET /api/companies/{ticker}/metrics`
- `GET /api/indicators/trends`
- `GET /api/transcripts/signals`
- `GET /api/risk-scores/history`
- `GET /api/alerts`
- `POST /api/manual-entry`

## Frontend

The React app exposes these routes:

- `/`: Overall AI CapEx Risk Dashboard
- `/companies/:ticker`: Company detail
- `/indicators`: Indicator trend page
- `/transcripts`: Transcript signal explorer
- `/risk-history`: Risk score history
- `/alerts`: Alerts page
- `/manual-entry`: Manual data entry

The dashboard emphasizes the current score, score change, bullish/bearish summaries, top indicators, hyperscaler capex trend, HBM/DRAM, CoWoS, data center/power, and financial stress.

## Testing

Unit tests cover:

- Weighted risk score calculation
- Risk band assignment
- Transcript keyword grouping
- Alert threshold behavior where practical

## Deployment

Include Docker support for the API and a compose file for local orchestration. README documents local .NET and frontend workflows first, with Docker as a convenience path.

## TODOs After MVP

- Connect SEC EDGAR XBRL ingestion.
- Add real transcript provider ingestion.
- Add RSS/news feed ingestion and source reliability scoring.
- Add PostgreSQL provider configuration.
- Add scheduled ingestion jobs.
- Add authentication and role-scoped manual entry.
- Add richer charting and export workflows.
