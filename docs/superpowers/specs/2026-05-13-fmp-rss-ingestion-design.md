# FMP And RSS Ingestion Design

## Goal

Add real earnings-call transcript ingestion via Financial Modeling Prep and RSS/news ingestion while keeping the app usable without API keys or network access.

## Design

FMP ingestion follows the SEC pattern: an interface-backed provider fetches transcript metadata/text when `FMP_API_KEY` is configured, and the importer stores deduped `Transcript`, `TranscriptMention`, and `SourceDocument` rows. Missing keys or provider errors return a non-fatal import result and preserve seeded/manual transcript data.

RSS ingestion reads configured feeds from `appsettings.json`, parses RSS/Atom XML with platform XML APIs, dedupes by URL, stores articles as `SourceDocument`, and creates keyword-derived `IndicatorSignal` rows. It does not require a paid API key.

The API gains `POST /api/import/transcripts/{ticker}` and `POST /api/import/rss`. The Data Sources page gets import controls for FMP and RSS, and Source Documents/Transcript Explorer immediately reflect imported items.

## Testing

Tests cover missing FMP key fallback, FMP JSON mapping, transcript dedupe/keyword analysis, RSS XML parsing, RSS dedupe, and keyword signal creation.
