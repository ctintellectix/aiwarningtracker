# Real SEC Ingestion Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a working SEC EDGAR ingestion vertical slice while keeping the dashboard runnable with seeded/cached data.

**Architecture:** Extend the existing clean architecture projects with SEC-specific domain entities, application interfaces/DTOs, infrastructure clients/importers, and API endpoints. Keep SQLite compatibility by creating new tables if missing and adding nullable columns to existing tables during startup.

**Tech Stack:** .NET 10, ASP.NET Core Web API, EF Core SQLite, official SEC JSON APIs, React/Vite.

---

### Task 1: SEC Parser Tests

- [x] Add tests for companyfacts parsing and metric extraction.
- [x] Verify tests fail before implementation.

### Task 2: Domain And Persistence

- [ ] Add `SecFiling` and `CompanyFact` entities.
- [ ] Extend `Company`, `FinancialMetric`, `SourceDocument`, `IndicatorSignal`, and `RiskScoreSnapshot` for source-attributed ingestion fields.
- [ ] Add SQLite schema upgrade helper for existing dev databases.

### Task 3: SEC Services

- [ ] Add `ISecClient`, `SecClient`, `ISecTickerCikMapper`, `SecTickerCikMapper`, `ISecCompanyFactImporter`, and `SecCompanyFactImporter`.
- [ ] Add raw JSON cache fallback and descriptive SEC User-Agent configuration.
- [ ] Extract capex, OCF, revenue, and debt metrics.

### Task 4: API And Frontend

- [ ] Add `POST /api/import/sec/{ticker}`, `GET /api/settings/data-sources`, and `GET /api/companies/{ticker}/financials`.
- [ ] Add simple frontend Data Sources and Company Financials views.
- [ ] Update README.

### Task 5: Verification

- [ ] Run `dotnet test`.
- [ ] Run frontend build.
- [ ] Smoke-test SEC import fallback path and financials endpoint.
