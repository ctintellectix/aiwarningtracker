# AI CapEx Slowdown Monitor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a running full-stack MVP for monitoring AI infrastructure capex slowdown risk with seeded data, scoring, alerts, API endpoints, and a React dashboard.

**Architecture:** Use .NET 8 clean architecture projects for domain, application, infrastructure, and API layers. Use EF Core SQLite with provider setup isolated in infrastructure, plus a Vite React TypeScript frontend consuming the API.

**Tech Stack:** C# / .NET 8, ASP.NET Core Web API, EF Core SQLite, xUnit, React, TypeScript, Vite, Docker.

---

### Task 1: Scaffold Solution

**Files:**
- Create: `AiCapexSlowdownMonitor.sln`
- Create: `src/AiCapex.Domain/AiCapex.Domain.csproj`
- Create: `src/AiCapex.Application/AiCapex.Application.csproj`
- Create: `src/AiCapex.Infrastructure/AiCapex.Infrastructure.csproj`
- Create: `src/AiCapex.Api/AiCapex.Api.csproj`
- Create: `tests/AiCapex.Application.Tests/AiCapex.Application.Tests.csproj`

- [ ] Create solution and projects with `dotnet new`.
- [ ] Add project references so API depends on Application and Infrastructure, Application depends on Domain, Infrastructure depends on Application and Domain, and tests depend on Application and Domain.
- [ ] Add EF Core SQLite packages to Infrastructure.
- [ ] Add ASP.NET Core OpenAPI support to API.

### Task 2: Score Model With TDD

**Files:**
- Create: `tests/AiCapex.Application.Tests/RiskScoring/RiskScoreCalculatorTests.cs`
- Create: `src/AiCapex.Domain/Scoring/RiskScoreCategory.cs`
- Create: `src/AiCapex.Domain/Scoring/RiskScoreWeights.cs`
- Create: `src/AiCapex.Application/Scoring/RiskScoreCalculator.cs`

- [ ] Write failing tests for weighted score calculation, clamping, and risk bands.
- [ ] Run tests and verify expected failures.
- [ ] Implement minimal scoring types and calculator.
- [ ] Run tests and verify pass.

### Task 3: Transcript Analyzer With TDD

**Files:**
- Create: `tests/AiCapex.Application.Tests/Transcripts/KeywordTranscriptAnalyzerTests.cs`
- Create: `src/AiCapex.Domain/Transcripts/TranscriptKeywordGroup.cs`
- Create: `src/AiCapex.Application/Transcripts/KeywordTranscriptAnalyzer.cs`

- [ ] Write failing tests for keyword group counts and bearish/bullish grouping.
- [ ] Run tests and verify expected failures.
- [ ] Implement keyword analyzer.
- [ ] Run tests and verify pass.

### Task 4: Domain Entities And Infrastructure

**Files:**
- Create domain entity files under `src/AiCapex.Domain/Entities/`.
- Create: `src/AiCapex.Infrastructure/Persistence/AiCapexDbContext.cs`
- Create: `src/AiCapex.Infrastructure/Persistence/SeedData.cs`
- Create: `src/AiCapex.Infrastructure/DependencyInjection.cs`

- [ ] Add all required entities and enums.
- [ ] Configure EF Core relationships and conversions.
- [ ] Seed companies, fiscal quarters, metrics, transcript mentions, indicator signals, score snapshots, source documents, and alerts.
- [ ] Register SQLite DbContext and seed on startup.

### Task 5: Application Services And API

**Files:**
- Create DTOs under `src/AiCapex.Application/Dashboard/`.
- Create services under `src/AiCapex.Application/`.
- Create controllers under `src/AiCapex.Api/Controllers/`.
- Modify: `src/AiCapex.Api/Program.cs`
- Modify: `src/AiCapex.Api/appsettings.json`

- [ ] Add dashboard summary, company, indicator, transcript, score history, alert, and manual entry services.
- [ ] Add ingestion interfaces and stub implementations.
- [ ] Add controllers for required API endpoints.
- [ ] Add configurable scoring weights in appsettings.
- [ ] Enable CORS for local frontend.

### Task 6: Frontend Dashboard

**Files:**
- Create: `frontend/package.json`
- Create: `frontend/src/App.tsx`
- Create: `frontend/src/api.ts`
- Create pages and styles under `frontend/src/`.

- [ ] Scaffold Vite React TypeScript app.
- [ ] Build dashboard, company detail, indicators, transcripts, risk history, alerts, and manual entry pages.
- [ ] Add clean responsive CSS with compact dashboard cards and tables.
- [ ] Wire all pages to API endpoints.

### Task 7: Docker, README, Verification

**Files:**
- Create: `Dockerfile`
- Create: `docker-compose.yml`
- Create: `README.md`

- [ ] Add API Dockerfile.
- [ ] Add compose file for API and frontend.
- [ ] Document local setup and TODOs for real data sources.
- [ ] Run `dotnet test`.
- [ ] Run frontend build.
- [ ] Start local dev servers if possible and report URLs.
