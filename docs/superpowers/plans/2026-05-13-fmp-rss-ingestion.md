# FMP And RSS Ingestion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add working FMP transcript and RSS/news import paths with fallback behavior.

**Architecture:** Add provider/client interfaces in Application, concrete HTTP/XML implementations in Infrastructure, and import endpoints in Api. Use existing transcript keyword analysis and source-document storage.

**Tech Stack:** C#/.NET, ASP.NET Core Web API, EF Core SQLite, React/Vite.

---

### Task 1: Transcript Provider And Importer

**Files:**
- Create: `src/AiCapex.Application/Ingestion/ITranscriptImportService.cs`
- Create: `src/AiCapex.Infrastructure/Transcripts/FmpTranscriptProvider.cs`
- Create: `src/AiCapex.Infrastructure/Transcripts/TranscriptImportService.cs`
- Test: `tests/AiCapex.Application.Tests/Ingestion/FmpTranscriptProviderTests.cs`
- Test: `tests/AiCapex.Application.Tests/Ingestion/TranscriptImportServiceTests.cs`

- [ ] Write failing tests for missing API key fallback and FMP JSON mapping.
- [ ] Implement FMP provider using configured `FMP_API_KEY`.
- [ ] Write failing test for deduped transcript import with keyword mentions.
- [ ] Implement transcript import service.
- [ ] Register services in DI and add API endpoint.

### Task 2: RSS Feed Import

**Files:**
- Create: `src/AiCapex.Application/Ingestion/IRssImportService.cs`
- Create: `src/AiCapex.Infrastructure/News/RssFeedClient.cs`
- Create: `src/AiCapex.Infrastructure/News/RssImportService.cs`
- Test: `tests/AiCapex.Application.Tests/Ingestion/RssFeedClientTests.cs`
- Test: `tests/AiCapex.Application.Tests/Ingestion/RssImportServiceTests.cs`

- [ ] Write failing tests for RSS/Atom parsing.
- [ ] Implement XML feed parser without adding packages.
- [ ] Write failing test for dedupe and signal creation.
- [ ] Implement RSS import service.
- [ ] Register services and add API endpoint.

### Task 3: Frontend And Docs

**Files:**
- Modify: `frontend/src/api.ts`
- Modify: `frontend/src/App.tsx`
- Modify: `README.md`

- [ ] Add client methods and Data Sources import buttons.
- [ ] Show import results clearly without exposing API keys.
- [ ] Update README with FMP/RSS setup and manual import commands.
- [ ] Run backend tests, frontend build, and endpoint smoke tests.
