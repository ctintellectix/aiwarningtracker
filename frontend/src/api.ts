const API_BASE = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5087";

export type RiskCategory =
  | "HyperscalerCapexRevisionTrend"
  | "HbmDramPricingAllocation"
  | "CowosAdvancedPackaging"
  | "DataCenterPower"
  | "AiRevenueMonetization"
  | "FinancialStressFreeCashFlow";

export type Signal = {
  ticker: string | null;
  quarter: string;
  category: RiskCategory;
  name: string;
  direction: "Bullish" | "Neutral" | "Bearish";
  scoreImpact: number;
  summary: string;
};

export type CategoryStatus = {
  category: RiskCategory;
  averageSignal: number;
  status: string;
  summary: string;
};

export type QuarterScore = {
  quarter: string;
  score: number;
  change: number;
  band: string;
};

export type DashboardSummary = {
  currentRiskScore: number;
  changeVsPreviousQuarter: number;
  riskBand: string;
  bullishSummary: string;
  bearishSummary: string;
  topPositiveIndicators: Signal[];
  topNegativeIndicators: Signal[];
  categoryStatuses: CategoryStatus[];
  scoreHistory: QuarterScore[];
};

export type Company = {
  id: number;
  ticker: string;
  name: string;
  segment: string;
  latestRiskSignal: number;
};

export type Metric = {
  quarter: string;
  kind: string;
  value: number;
  unit: string;
};

export type SourceDocument = {
  ticker: string | null;
  sourceType: string;
  title: string;
  url: string;
  summary: string;
  publishedDate: string;
};

export type CompanyDetail = {
  company: Company;
  metrics: Metric[];
  signals: Signal[];
  sources: SourceDocument[];
};

export type TranscriptSignal = {
  ticker: string;
  quarter: string;
  title: string;
  keywordGroup: string;
  count: number;
  publishedDate: string;
};

export type TranscriptResult = {
  ticker: string;
  fiscalYear: number;
  fiscalQuarter: number;
  market: string | null;
  callDate: string | null;
  provider: string;
  title: string;
  rawText: string;
  sourceUrl: string | null;
  confidenceScore: number;
};

export type Alert = {
  id: number;
  severity: "Info" | "Warning" | "Critical";
  title: string;
  message: string;
  createdAt: string;
  isAcknowledged: boolean;
};

export type ManualEntry = {
  ticker: string;
  category: string;
  scoreImpact: number;
  summary: string;
  sourceTitle: string;
};

export type DataSourceStatus = {
  source: string;
  isConfigured: boolean;
  lastSuccessfulImport: string | null;
  message: string;
};

export type SecImportResult = {
  ticker: string;
  usedLiveData: boolean;
  factsImported: number;
  metricsImported: number;
  message: string;
};

export type ImportResult = {
  source: string;
  isConfigured: boolean;
  documentsImported: number;
  signalsImported: number;
  message: string;
  itemsFetched: number;
  documentsSkipped: number;
};

export type BulkImportItem = {
  ticker: string;
  success: boolean;
  message: string;
  documentsImported: number;
  signalsImported: number;
};

export type BulkImportResult = {
  source: string;
  companiesProcessed: number;
  successCount: number;
  failureCount: number;
  documentsImported: number;
  signalsImported: number;
  results: BulkImportItem[];
};

export type AllImportResult = {
  sec: BulkImportResult;
  transcripts: BulkImportResult;
  rss: ImportResult;
};

export type CompanyFinancials = {
  company: Company;
  capex: Metric[];
  operatingCashFlow: Metric[];
  capexRatio: Metric[];
  revenue: Metric[];
  debt: Metric[];
  sources: SourceDocument[];
};

async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`);
  if (!response.ok) {
    throw new Error(`Request failed: ${response.status}`);
  }
  return response.json() as Promise<T>;
}

export const api = {
  summary: () => getJson<DashboardSummary>("/api/dashboard/summary"),
  companies: () => getJson<Company[]>("/api/companies"),
  company: (ticker: string) => getJson<CompanyDetail>(`/api/companies/${ticker}`),
  indicators: () => getJson<CategoryStatus[]>("/api/indicators/trends"),
  transcripts: () => getJson<TranscriptSignal[]>("/api/transcripts/signals"),
  history: () => getJson<QuarterScore[]>("/api/risk-scores/history"),
  alerts: () => getJson<Alert[]>("/api/alerts"),
  dataSources: () => getJson<DataSourceStatus[]>("/api/settings/data-sources"),
  importSec: async (ticker: string) => {
    const response = await fetch(`${API_BASE}/api/import/sec/${ticker}`, { method: "POST" });
    if (!response.ok) {
      throw new Error(`SEC import failed: ${response.status}`);
    }
    return response.json() as Promise<SecImportResult>;
  },
  importSecAll: async () => {
    const response = await fetch(`${API_BASE}/api/import/sec/all`, { method: "POST" });
    if (!response.ok) {
      throw new Error(`Bulk SEC import failed: ${response.status}`);
    }
    return response.json() as Promise<BulkImportResult>;
  },
  transcript: (ticker: string, year: number, quarter: number) => getJson<TranscriptResult>(`/api/transcripts/${ticker}/${year}/${quarter}`),
  importRss: async () => {
    const response = await fetch(`${API_BASE}/api/import/rss`, { method: "POST" });
    if (!response.ok) {
      throw new Error(`RSS import failed: ${response.status}`);
    }
    return response.json() as Promise<ImportResult>;
  },
  importAll: async () => {
    const response = await fetch(`${API_BASE}/api/import/all`, { method: "POST" });
    if (!response.ok) {
      throw new Error(`Bulk import failed: ${response.status}`);
    }
    return response.json() as Promise<AllImportResult>;
  },
  companyFinancials: (ticker: string) => getJson<CompanyFinancials>(`/api/companies/${ticker}/financials`),
  manualEntry: async (entry: ManualEntry) => {
    const response = await fetch(`${API_BASE}/api/manual-entry`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(entry)
    });
    if (!response.ok) {
      throw new Error(`Manual entry failed: ${response.status}`);
    }
    return response.json() as Promise<Signal>;
  }
};
