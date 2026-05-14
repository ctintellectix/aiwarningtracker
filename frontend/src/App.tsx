import { AlertTriangle, BarChart3, Building2, ChevronDown, ChevronRight, FileText, Gauge, HelpCircle, PlusCircle, RadioTower } from "lucide-react";
import { FormEvent, useEffect, useState } from "react";
import { Alert, api, CategoryStatus, CompanyDetail, DashboardSummary, DataSourceStatus, ManualEntry, QuarterScore, TranscriptSignal } from "./api";

const nav = [
  ["/", "Dashboard", Gauge],
  ["/indicators", "Indicators", BarChart3],
  ["/transcripts", "Transcripts", FileText],
  ["/risk-history", "History", RadioTower],
  ["/alerts", "Alerts", AlertTriangle],
  ["/data-sources", "Data Sources", Building2],
  ["/manual-entry", "Manual Entry", PlusCircle]
] as const;

export function App() {
  const [path, setPath] = useState(window.location.pathname);
  const [refreshKey, setRefreshKey] = useState(0);

  useEffect(() => {
    const onPop = () => setPath(window.location.pathname);
    window.addEventListener("popstate", onPop);
    return () => window.removeEventListener("popstate", onPop);
  }, []);

  const go = (to: string) => {
    window.history.pushState(null, "", to);
    setPath(to);
  };
  const refreshStats = () => setRefreshKey((value) => value + 1);

  return (
    <div className="app">
      <aside>
        <div className="brand">
          <Gauge size={28} />
          <div>
            <strong>AI CapEx</strong>
            <span>Slowdown Monitor</span>
          </div>
        </div>
        <nav>
          {nav.map(([href, label, Icon]) => (
            <button className={path === href ? "active" : ""} key={href} onClick={() => go(href)}>
              <Icon size={18} />
              {label}
            </button>
          ))}
        </nav>
      </aside>
      <main>
        {path === "/" && <Dashboard go={go} refreshKey={refreshKey} />}
        {path.startsWith("/companies/") && <CompanyPage ticker={path.split("/").pop() ?? "MSFT"} refreshKey={refreshKey} />}
        {path === "/indicators" && <IndicatorsPage refreshKey={refreshKey} />}
        {path === "/transcripts" && <TranscriptsPage refreshKey={refreshKey} />}
        {path === "/risk-history" && <HistoryPage refreshKey={refreshKey} />}
        {path === "/alerts" && <AlertsPage refreshKey={refreshKey} />}
        {path === "/data-sources" && <DataSourcesPage onDataRefresh={refreshStats} refreshKey={refreshKey} />}
        {path === "/manual-entry" && <ManualEntryPage onDataRefresh={refreshStats} />}
      </main>
    </div>
  );
}

function Dashboard({ go, refreshKey }: { go: (path: string) => void; refreshKey: number }) {
  const summary = useApi(api.summary, [refreshKey]);
  const companies = useApi(api.companies, [refreshKey]);
  if (!summary.data || !companies.data) return <Loading error={summary.error ?? companies.error} />;
  const data = summary.data;

  return (
    <>
      <Header title="Overall AI CapEx Momentum Dashboard" subtitle="Real public-signal monitor for AI infrastructure capex momentum." />
      <section className="panel">
        <div className="panelHead">
          <h2>Tracked companies</h2>
        </div>
        <div className="companyGrid">
          {companies.data.map((company) => (
            <button className="company" key={company.ticker} onClick={() => go(`/companies/${company.ticker}`)}>
              <Building2 size={18} />
              <strong>{company.ticker}</strong>
              <span>{company.segment}</span>
            </button>
          ))}
        </div>
      </section>
      <section className="scoreBand">
        <div className="scoreDial">
          <div className="labelWithTooltip">
            <span className="scoreValue">{data.currentRiskScore}</span>
            <TooltipIcon text={tooltips.riskScore} />
          </div>
          <small>{data.riskBand}</small>
        </div>
        <MetricCard label="Momentum change vs previous quarter" value={`${signed(data.changeVsPreviousQuarter)} pts`} tooltip={tooltips.riskScoreChange} />
        <MetricCard label="Bullish signal summary" value={data.bullishSummary} text />
        <MetricCard label="Bearish signal summary" value={data.bearishSummary} text />
      </section>
      <section className="grid two">
        <SignalList title="Top positive indicators" signals={data.topPositiveIndicators} />
        <SignalList title="Top negative indicators" signals={data.topNegativeIndicators} />
      </section>
      <section className="grid categories">
        {data.categoryStatuses.map((item) => (
          <article className="panel" key={item.category}>
            <h3>{labelCategory(item.category)}</h3>
            <div className="labelWithTooltip">
              <strong className={item.averageSignal < -20 ? "bad" : item.averageSignal > 10 ? "good" : ""}>{item.status}</strong>
              <TooltipIcon text={tooltips.categorySignal} />
            </div>
            <p>{item.summary}</p>
          </article>
        ))}
      </section>
    </>
  );
}

function CompanyPage({ ticker, refreshKey }: { ticker: string; refreshKey: number }) {
  const detail = useApi(() => api.company(ticker), [ticker, refreshKey]);
  const financials = useApi(() => api.companyFinancials(ticker), [ticker, refreshKey]);
  if (!detail.data) return <Loading error={detail.error} />;
  const { company, metrics, signals, sources } = detail.data;
  return (
    <>
      <Header title={`${company.ticker} - ${company.name}`} subtitle={company.segment} />
      <section className="grid three">
        <MetricCard label="Latest risk signal" value={company.latestRiskSignal.toFixed(1)} tooltip={tooltips.companyRiskSignal} />
        <MetricCard label="Signal count" value={signals.length.toString()} tooltip={tooltips.signalCount} />
        <MetricCard label="Source docs" value={sources.length.toString()} tooltip={tooltips.sourceDocs} />
      </section>
      <CollapsibleDataTable title="Financial metrics" defaultOpen={false} rows={metrics.map((m) => [m.quarter, prettyMetric(m.kind), `${m.value} ${m.unit}`])} headers={["Quarter", "Metric", "Value"]} headerTooltips={{ Value: tooltips.financialMetricValue }} />
      {financials.data && (
        <section className="grid two">
          <MiniMetricChart title="Capex Trend" metrics={financials.data.capex.filter((m) => m.kind === "Quarterly Capex" || m.kind === "QuarterlyCapex")} />
          <MiniMetricChart title="Capex / OCF" metrics={financials.data.capexRatio} />
          <MiniMetricChart title="Revenue Trend" metrics={financials.data.revenue} />
          <MiniMetricChart title="Debt Trend" metrics={financials.data.debt} />
        </section>
      )}
      <SignalList title="Company signals" signals={signals} />
      <DataTable title="Sources" rows={sources.map((s) => [s.sourceType, s.title, s.summary])} headers={["Type", "Title", "Summary"]} />
    </>
  );
}

function IndicatorsPage({ refreshKey }: { refreshKey: number }) {
  const indicators = useApi(api.indicators, [refreshKey]);
  if (!indicators.data) return <Loading error={indicators.error} />;
  return (
    <>
      <Header title="Indicator Trend Page" subtitle="Weighted categories behind the current AI capex expansion score." />
      <div className="grid two">
        {indicators.data.map((item: CategoryStatus) => (
          <article className="panel" key={item.category}>
            <h2>{labelCategory(item.category)}</h2>
            <div className="bar" title={tooltips.categorySignal}><span style={{ width: `${Math.min(100, Math.abs(item.averageSignal))}%` }} /></div>
            <p className="labelWithTooltip"><strong>{item.status}</strong> ({item.averageSignal}) <TooltipIcon text={tooltips.categorySignal} /></p>
            <p>{item.summary}</p>
          </article>
        ))}
      </div>
    </>
  );
}

function TranscriptsPage({ refreshKey }: { refreshKey: number }) {
  const transcripts = useApi(api.transcripts, [refreshKey]);
  if (!transcripts.data) return <Loading error={transcripts.error} />;
  return (
    <>
      <Header title="Transcript Signal Explorer" subtitle="Keyword-based mentions from imported transcript text." />
      <DataTable title="Transcript mentions" rows={transcripts.data.map((t: TranscriptSignal) => [t.ticker, t.quarter, t.keywordGroup, t.count.toString(), t.title])} headers={["Ticker", "Quarter", "Group", "Count", "Transcript"]} headerTooltips={{ Count: tooltips.transcriptMentionCount }} />
    </>
  );
}

function HistoryPage({ refreshKey }: { refreshKey: number }) {
  const history = useApi(api.history, [refreshKey]);
  if (!history.data) return <Loading error={history.error} />;
  return (
    <>
      <Header title="Expansion Score History" subtitle="Quarterly score snapshots generated from real and manual category signals." />
      <section className="panel chart">
        {history.data.map((point: QuarterScore) => (
          <div className="column" key={point.quarter} title={tooltips.riskScore}>
            <span className={scoreClass(point.score)} style={{ height: `${Math.max(point.score * 2, 12)}px` }} />
            <strong>{point.score}</strong>
            <small>{point.quarter}</small>
          </div>
        ))}
      </section>
      <DataTable title="History" rows={history.data.map((h) => [h.quarter, h.score.toString(), signed(h.change), h.band])} headers={["Quarter", "Score", "Change", "Band"]} headerTooltips={{ Score: tooltips.riskScore, Change: tooltips.riskScoreChange, Band: tooltips.riskBand }} />
    </>
  );
}

function AlertsPage({ refreshKey }: { refreshKey: number }) {
  const alerts = useApi(api.alerts, [refreshKey]);
  if (!alerts.data) return <Loading error={alerts.error} />;
  return (
    <>
      <Header title="Alerts" subtitle="Watchlist conditions triggered by current real and manual signals." />
      <div className="stack">
        {alerts.data.length === 0 ? (
          <section className="panel">
            <p className="emptyState">No alerts yet. Run imports and scoring, or add manual signals, to populate watchlist alerts.</p>
          </section>
        ) : (
          alerts.data.map((alert: Alert) => (
            <article className={`panel alert ${alert.severity.toLowerCase()}`} key={alert.id}>
              <strong>{alert.title}</strong>
              <span>{alert.severity}</span>
              <p>{alert.message}</p>
            </article>
          ))
        )}
      </div>
    </>
  );
}

function DataSourcesPage({ onDataRefresh, refreshKey }: { onDataRefresh: () => void; refreshKey: number }) {
  const statuses = useApi(api.dataSources, [refreshKey]);
  const companies = useApi(api.companies, [refreshKey]);
  const [ticker, setTicker] = useState("MSFT");
  const [result, setResult] = useState("");
  if (!statuses.data || !companies.data) return <Loading error={statuses.error ?? companies.error} />;

  const runSecImport = async () => {
    setResult("Importing SEC companyfacts...");
    const response = await api.importSec(ticker);
    setResult(`${response.ticker}: ${response.message} ${response.factsImported} facts, ${response.metricsImported} metrics.`);
    onDataRefresh();
  };

  const runRssImport = async () => {
    setResult("Importing RSS/news feeds...");
    const response = await api.importRss();
    setResult(`${response.source}: ${response.message} ${response.signalsImported} signals.`);
    onDataRefresh();
  };

  const runSecAll = async () => {
    setResult("Importing SEC companyfacts for all tracked companies...");
    const response = await api.importSecAll();
    setResult(`${response.source}: processed ${response.companiesProcessed}, ${response.successCount} succeeded, ${response.failureCount} failed, ${response.documentsImported} facts, ${response.signalsImported} metrics.`);
    onDataRefresh();
  };

  const runAllImports = async () => {
    setResult("Running SEC, transcript, and RSS imports for all tracked companies...");
    const response = await api.importAll();
    setResult(`All imports finished. SEC: ${response.sec.successCount}/${response.sec.companiesProcessed} succeeded. Transcripts: ${response.transcripts.documentsImported} documents. RSS: ${response.rss.message}`);
    onDataRefresh();
  };

  return (
    <>
      <Header title="Data Sources" subtitle="Configuration status and manual import controls." />
      <section className="grid three">
        {statuses.data.map((status: DataSourceStatus) => (
          <article className="panel" key={status.source}>
            <h2>{status.source}</h2>
            <strong className={status.isConfigured ? "good" : "bad"}>{status.isConfigured ? "Configured" : "Missing config"}</strong>
            <p>{status.message}</p>
            <small>{status.lastSuccessfulImport ? `Last import: ${new Date(status.lastSuccessfulImport).toLocaleString()}` : "No imports yet"}</small>
          </article>
        ))}
      </section>
      <section className="panel form">
        <h2>SEC EDGAR import</h2>
        <label>Company<select value={ticker} onChange={(event) => setTicker(event.target.value)}>{companies.data.map((company) => <option key={company.ticker} value={company.ticker}>{company.ticker} - {company.name}</option>)}</select></label>
        <div className="buttonRow">
          <button className="primary" type="button" onClick={runSecImport}>Run SEC import</button>
          <button type="button" onClick={runSecAll}>Run SEC for all tracked</button>
          <button type="button" onClick={runRssImport}>Run RSS import</button>
          <button type="button" onClick={runAllImports}>Run all imports</button>
        </div>
        {result && <p>{result}</p>}
      </section>
    </>
  );
}

function ManualEntryPage({ onDataRefresh }: { onDataRefresh: () => void }) {
  const [status, setStatus] = useState("");
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const entry: ManualEntry = {
      ticker: String(form.get("ticker")),
      category: String(form.get("category")),
      scoreImpact: Number(form.get("scoreImpact")),
      sourceTitle: String(form.get("sourceTitle")),
      summary: String(form.get("summary"))
    };
    await api.manualEntry(entry);
    setStatus("Entry saved.");
    event.currentTarget.reset();
    onDataRefresh();
  };

  return (
    <>
      <Header title="Manual Data Entry" subtitle="Add analyst-observed indicator signals to the current quarter." />
      <form className="panel form" onSubmit={submit}>
        <label>Ticker<input name="ticker" defaultValue="MSFT" required /></label>
        <label>Category<select name="category" defaultValue="HyperscalerCapexRevisionTrend">{categoryOptions.map((x) => <option key={x} value={x}>{labelCategory(x)}</option>)}</select></label>
        <label><span className="labelWithTooltip">Score impact<TooltipIcon text={tooltips.signalImpact} /></span><input name="scoreImpact" type="number" min="-100" max="100" defaultValue="-20" required /></label>
        <label>Source title<input name="sourceTitle" defaultValue="Manual analyst note" required /></label>
        <label>Summary<textarea name="summary" defaultValue="Capex commentary moved from raise to hold." required /></label>
        <button className="primary">Save signal</button>
        {status && <p className="good">{status}</p>}
      </form>
    </>
  );
}

function Header({ title, subtitle }: { title: string; subtitle: string }) {
  return <header><h1>{title}</h1><p>{subtitle}</p></header>;
}

function MetricCard({ label, value, text = false, tooltip }: { label: string; value: string; text?: boolean; tooltip?: string }) {
  return <article className="metric"><span className="labelWithTooltip">{label}{tooltip && <TooltipIcon text={tooltip} />}</span><strong className={text ? "textValue" : ""}>{value}</strong></article>;
}

function SignalList({ title, signals }: { title: string; signals: DashboardSummary["topPositiveIndicators"] }) {
  return (
    <section className="panel">
      <h2 className="labelWithTooltip">{title}<TooltipIcon text={tooltips.signalImpact} /></h2>
      {signals.length === 0 ? (
        <p className="emptyState">No directional indicators yet. Run imports or add manual signals with non-zero score impact.</p>
      ) : (
        <div className="stack">{signals.map((s) => <article className="signal" key={`${s.name}-${s.ticker}`}><strong>{s.name}</strong><span className={s.direction.toLowerCase()} title={tooltips.signalImpact}>{s.direction} {signed(s.scoreImpact)} <TooltipIcon text={tooltips.signalImpact} /></span><p>{s.summary}</p></article>)}</div>
      )}
    </section>
  );
}

function DataTable({ title, headers, rows, headerTooltips = {} }: { title: string; headers: string[]; rows: string[][]; headerTooltips?: Record<string, string> }) {
  return <section className="panel"><h2>{title}</h2><div className="tableWrap"><table><thead><tr>{headers.map((h) => <th key={h}>{h}{headerTooltips[h] && <TooltipIcon text={headerTooltips[h]} />}</th>)}</tr></thead><tbody>{rows.map((row, index) => <tr key={index}>{row.map((cell, i) => <td key={i}>{cell}</td>)}</tr>)}</tbody></table></div></section>;
}

function CollapsibleDataTable({ title, headers, rows, headerTooltips = {}, defaultOpen = false }: { title: string; headers: string[]; rows: string[][]; headerTooltips?: Record<string, string>; defaultOpen?: boolean }) {
  const [open, setOpen] = useState(defaultOpen);
  const ToggleIcon = open ? ChevronDown : ChevronRight;
  return (
    <section className="panel">
      <div className="collapsibleHead">
        <h2>{title}</h2>
        <button type="button" className="iconButton" onClick={() => setOpen((value) => !value)} aria-expanded={open} aria-label={`${open ? "Collapse" : "Expand"} ${title}`} title={`${open ? "Collapse" : "Expand"} ${title}`}>
          <ToggleIcon size={18} />
        </button>
      </div>
      {!open && <p className="emptyState">{rows.length} rows hidden.</p>}
      {open && <div className="tableWrap"><table><thead><tr>{headers.map((h) => <th key={h}>{h}{headerTooltips[h] && <TooltipIcon text={headerTooltips[h]} />}</th>)}</tr></thead><tbody>{rows.map((row, index) => <tr key={index}>{row.map((cell, i) => <td key={i}>{cell}</td>)}</tr>)}</tbody></table></div>}
    </section>
  );
}

function TooltipIcon({ text }: { text: string }) {
  return <span className="tooltipIcon" tabIndex={0} role="img" aria-label={text} title={text}><HelpCircle size={14} /></span>;
}

function MiniMetricChart({ title, metrics }: { title: string; metrics: { quarter: string; value: number; unit: string }[] }) {
  const max = Math.max(...metrics.map((m) => Math.abs(m.value)), 1);
  return (
    <section className="panel miniChart">
      <h2>{title}</h2>
      <div>
        {metrics.slice(-8).map((metric) => (
          <span key={`${metric.quarter}-${metric.value}`} style={{ height: `${Math.max((Math.abs(metric.value) / max) * 150, 8)}px` }}>
            <small>{metric.value.toFixed(metric.unit === "%" ? 1 : 0)}</small>
          </span>
        ))}
      </div>
    </section>
  );
}

function Loading({ error }: { error?: string }) {
  return <section className="panel"><h2>{error ? "Could not load data" : "Loading"}</h2><p>{error ?? "Fetching dashboard data..."}</p></section>;
}

function useApi<T>(loader: () => Promise<T>, deps: unknown[] = []) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string>();
  useEffect(() => {
    let live = true;
    loader().then((result) => live && setData(result)).catch((err: Error) => live && setError(err.message));
    return () => { live = false; };
  }, deps);
  return { data, error };
}

const categoryOptions = ["HyperscalerCapexRevisionTrend", "HbmDramPricingAllocation", "CowosAdvancedPackaging", "DataCenterPower", "AiRevenueMonetization", "FinancialStressFreeCashFlow"] as const;

const tooltips = {
  riskScore: "0-100 AI CapEx expansion score. Higher means stronger expansion; lower means slowdown or capex rollover risk.",
  riskScoreChange: "Point change versus the previous fiscal quarter's expansion score. Positive means momentum improved; negative means momentum weakened.",
  riskBand: "Plain-English bucket for the 0-100 expansion score: rollover risk, slowdown forming, watch zone, healthy expansion, or bullish acceleration.",
  signalImpact: "Signal impact uses a -100 to +100 scale. Positive values are bullish for AI capex momentum; negative values are bearish or risk-increasing.",
  categorySignal: "Average signal impact for this category. Above +15 is constructive, below -25 is weakening, and values near zero are mixed.",
  companyRiskSignal: "Average score impact of this company's indicator signals. Positive is bullish; negative is bearish/risk-increasing.",
  signalCount: "Number of indicator signals currently stored for this company.",
  sourceDocs: "Number of source documents tied to this company, including SEC imports, RSS/news items, transcripts, and manual entries.",
  financialMetricValue: "Financial metric value imported for that fiscal quarter. Units vary by metric and source.",
  transcriptMentionCount: "Number of keyword hits found for this keyword group in the transcript text.",
  confidenceScore: "0-100 estimate of source/candidate quality. Higher means the app has more confidence the item is relevant and usable."
};

function labelCategory(category: string) {
  return category
    .replace("HbmDram", "HBM/DRAM ")
    .replace("Cowos", "CoWoS ")
    .replace("Ai", "AI ")
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace("Free Cash Flow", "FCF");
}

function prettyMetric(metric: string) {
  return metric.replace(/([a-z])([A-Z])/g, "$1 $2");
}

function signed(value: number) {
  return value > 0 ? `+${value}` : `${value}`;
}

function scoreClass(score: number) {
  if (score <= 24) return "scoreRollover";
  if (score <= 39) return "scoreSlowdown";
  if (score <= 54) return "scoreWatch";
  if (score <= 74) return "scoreHealthy";
  return "scoreBullish";
}
