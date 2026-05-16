import { AlertTriangle, Building2, ChevronDown, ChevronRight, Gauge, HelpCircle, PlusCircle, RadioTower } from "lucide-react";
import { FormEvent, useEffect, useState } from "react";
import { Alert, api, CategoryStatus, CompanyDetail, DashboardSummary, DataSourceStatus, ImportJob, ManualEntry, QuarterScore } from "./api";

const nav = [
  ["/", "Dashboard", Gauge],
  ["/risk-history", "History", RadioTower],
  ["/alerts", "Alerts", AlertTriangle],
  ["/data-sources", "Data Sources", Building2],
  ["/manual-entry", "Manual Entry", PlusCircle]
] as const;

const appBase = normalizeBase(import.meta.env.BASE_URL);

export function App() {
  const [path, setPath] = useState(routeFromLocation(window.location.pathname));
  const [refreshKey, setRefreshKey] = useState(0);

  useEffect(() => {
    const onPop = () => setPath(routeFromLocation(window.location.pathname));
    window.addEventListener("popstate", onPop);
    return () => window.removeEventListener("popstate", onPop);
  }, []);

  const go = (to: string) => {
    window.history.pushState(null, "", toAppPath(to));
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
      <section className="scoreBand">
        <div className="scoreDial">
          <div className="labelWithTooltip">
            <span className={`scoreValue ${scoreTextClass(data.currentRiskScore)}`}>{data.currentRiskScore}</span>
            <TooltipIcon text={tooltips.riskScore} />
          </div>
          <small>{data.riskBand}</small>
        </div>
        <MetricCard label="Momentum change vs previous quarter" value={`${signed(data.changeVsPreviousQuarter)} pts`} tooltip={tooltips.riskScoreChange} />
      </section>
      <section className="grid two">
        {data.categoryStatuses.map((item) => (
          <article className="panel" key={item.category}>
            <h2>{labelCategory(item.category)}</h2>
            <div className={`bar ${signalToneClass(item.averageSignal)}`} title={tooltips.categorySignal}><span style={{ width: `${Math.min(100, Math.abs(item.averageSignal) * 10)}%` }} /></div>
            <p className={`labelWithTooltip ${signalToneClass(item.averageSignal)}`}>
              <strong>{item.status}</strong> ({item.averageSignal}) <TooltipIcon text={tooltips.categorySignal} />
            </p>
            <p>{item.summary}</p>
          </article>
        ))}
      </section>
      <SignalList title="Top company drivers" signals={data.topCompanyDrivers} />
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
    </>
  );
}

function CompanyPage({ ticker, refreshKey }: { ticker: string; refreshKey: number }) {
  const detail = useApi(() => api.company(ticker), [ticker, refreshKey]);
  const financials = useApi(() => api.companyFinancials(ticker), [ticker, refreshKey]);
  if (!detail.data) return <Loading error={detail.error} />;
  const { company, metrics, signals, currentSignals, historicalSignals, sources } = detail.data;
  return (
    <>
      <Header title={`${company.ticker} - ${company.name}`} subtitle={company.segment} />
      <section className="grid three">
        <MomentumMetricCard value={company.latestMomentumSignal} />
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
      <SignalList title="Current category signals" signals={currentSignals} />
      <CollapsibleSignalList title="Historical category signals" signals={historicalSignals} />
      <DataTable title="Sources" rows={sources.map((s) => [s.sourceType, s.title, s.summary])} headers={["Type", "Title", "Summary"]} />
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
      <DataTable title="History" rows={history.data.map((h) => [h.quarter, h.score.toString(), h.change == null ? "-" : signed(h.change), h.band])} headers={["Quarter", "Score", "Change", "Band"]} headerTooltips={{ Score: tooltips.riskScore, Change: tooltips.riskScoreChange, Band: tooltips.riskBand }} />
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
  const [job, setJob] = useState<ImportJob | null>(null);

  useEffect(() => {
    if (!job || job.status === "Completed" || job.status === "Failed") return;
    const timer = window.setInterval(async () => {
      try {
        const next = await api.importJob(job.id);
        setJob(next);
        if (next.status === "Completed") {
          setResult(formatJobResult(next));
          onDataRefresh();
        }
        if (next.status === "Failed") {
          setResult(`Import failed: ${next.message}`);
        }
      } catch (error) {
        setResult(error instanceof Error ? error.message : "Could not refresh import status.");
      }
    }, 1500);
    return () => window.clearInterval(timer);
  }, [job, onDataRefresh]);

  if (!statuses.data || !companies.data) return <Loading error={statuses.error ?? companies.error} />;

  const runSecImport = async () => {
    setResult("Starting SEC companyfacts import...");
    setJob(await api.startSecImportJob(ticker));
  };

  const runRssImport = async () => {
    setResult("Starting RSS/news import...");
    setJob(await api.startRssImportJob());
  };

  const runSecAll = async () => {
    setResult("Starting SEC companyfacts import for all tracked companies...");
    setJob(await api.startSecAllImportJob());
  };

  const runTranscriptImport = async () => {
    setResult("Starting transcript import...");
    setJob(await api.startTranscriptImportJob());
  };

  const runAllImports = async () => {
    setResult("Starting full import run...");
    setJob(await api.startAllImportJob());
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
          <button type="button" onClick={runTranscriptImport}>Run transcript import</button>
          <button type="button" onClick={runAllImports}>Run all imports</button>
        </div>
        {job && job.status !== "Completed" && job.status !== "Failed" && (
          <div className="jobProgress">
            <div className="bar"><span style={{ width: `${job.progressPercent}%` }} /></div>
            <p>{job.kind}: {job.message} {job.progressPercent}%</p>
          </div>
        )}
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
        <label><span className="labelWithTooltip">Score impact<TooltipIcon text={tooltips.signalImpact} /></span><input name="scoreImpact" type="number" min="-10" max="10" defaultValue="-2" required /></label>
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

function MomentumMetricCard({ value }: { value: number }) {
  return (
    <article className="metric">
      <span className="labelWithTooltip">Latest momentum signal<TooltipIcon text={tooltips.companyRiskSignal} /></span>
      <strong className={signalToneClass(value)}>{signalBandLabel(value)} {signed(Number(value.toFixed(1)))}</strong>
    </article>
  );
}

function SignalList({ title, signals }: { title: string; signals: DashboardSummary["topPositiveIndicators"] }) {
  return (
    <section className="panel">
      <h2 className="labelWithTooltip">{title}<TooltipIcon text={tooltips.signalImpact} /></h2>
      {signals.length === 0 ? (
        <p className="emptyState">No directional score drivers yet. Run imports or add manual signals with non-zero score impact.</p>
      ) : (
        <div className="stack">
          {signals.map((s) => <SignalItem key={`${s.name}-${s.ticker}`} signal={s} />)}
        </div>
      )}
    </section>
  );
}

function CollapsibleSignalList({ title, signals }: { title: string; signals: DashboardSummary["topPositiveIndicators"] }) {
  const [open, setOpen] = useState(false);
  const ToggleIcon = open ? ChevronDown : ChevronRight;
  return (
    <section className="panel">
      <div className="collapsibleHead">
        <h2 className="labelWithTooltip">{title}<TooltipIcon text={tooltips.signalImpact} /></h2>
        <button type="button" className="iconButton" onClick={() => setOpen((value) => !value)} aria-expanded={open} aria-label={`${open ? "Collapse" : "Expand"} ${title}`} title={`${open ? "Collapse" : "Expand"} ${title}`}>
          <ToggleIcon size={18} />
        </button>
      </div>
      {!open && <p className="emptyState">{signals.length} older signals hidden.</p>}
      {open && (
        signals.length === 0 ? (
          <p className="emptyState">No historical signals yet.</p>
        ) : (
          <div className="stack">{signals.map((s) => <SignalItem key={`${s.name}-${s.ticker}-${s.quarter}`} signal={s} />)}</div>
        )
      )}
    </section>
  );
}

function SignalItem({ signal }: { signal: DashboardSummary["topPositiveIndicators"][number] }) {
  return (
    <article className="signal">
      <strong>{signal.ticker ? `${signal.ticker} - ${signal.name}` : signal.name}</strong>
      {signal.ticker && <small>{labelCategory(signal.category)} | {signal.quarter} | {signal.sourceLabel}</small>}
      <span className={signalToneClass(signal.scoreImpact)} title={tooltips.signalImpact}>
        {signalBandLabel(signal.scoreImpact)} {signed(signal.scoreImpact)} <TooltipIcon text={tooltips.signalImpact} />
      </span>
      <p>{signal.summary}</p>
    </article>
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
            <small>{formatMiniMetricValue(metric.value, metric.unit)}</small>
          </span>
        ))}
      </div>
    </section>
  );
}

function formatMiniMetricValue(value: number, unit: string) {
  if (unit === "%") {
    return `${value.toFixed(1)}%`;
  }

  const absolute = Math.abs(value);
  const compactUnits = [
    { threshold: 1_000_000_000_000, suffix: "T" },
    { threshold: 1_000_000_000, suffix: "B" },
    { threshold: 1_000_000, suffix: "M" },
    { threshold: 1_000, suffix: "K" }
  ];
  const compactUnit = compactUnits.find((item) => absolute >= item.threshold);

  if (!compactUnit) {
    return value.toFixed(0);
  }

  const scaled = value / compactUnit.threshold;
  const decimals = Math.abs(scaled) < 10 ? 1 : 0;
  return `${scaled.toFixed(decimals)}${compactUnit.suffix}`;
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
  riskBand: "Plain-English bucket for the 0-100 expansion score: very weak, weak, neutral, strong, or very strong.",
  signalImpact: "Signal impact uses a trader-facing -10 to +10 momentum scale. Positive values support AI capex momentum; negative values weaken it.",
  categorySignal: "Average category momentum on a -10 to +10 scale. Bearish is below -1, bullish is above +1, and values near zero are neutral.",
  companyRiskSignal: "Current company momentum signal on a -10 to +10 scale, using the latest evidence by category. Positive is bullish; negative is bearish.",
  signalCount: "Number of indicator signals currently stored for this company.",
  sourceDocs: "Number of source documents tied to this company, including SEC imports, RSS/news items, transcripts, and manual entries.",
  financialMetricValue: "Financial metric value imported for that fiscal quarter. Units vary by metric and source.",
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

function signalToneClass(score: number) {
  if (score < -1) return "bad";
  if (score > 1) return "good";
  return "neutral";
}

function signalBandLabel(score: number) {
  if (score <= -6) return "Very bearish";
  if (score < -1) return "Bearish";
  if (score <= 1) return "Neutral";
  if (score < 6) return "Bullish";
  return "Very bullish";
}

function scoreClass(score: number) {
  if (score <= 19) return "scoreRollover";
  if (score <= 39) return "scoreSlowdown";
  if (score <= 59) return "scoreWatch";
  if (score <= 79) return "scoreHealthy";
  return "scoreBullish";
}

function scoreTextClass(score: number) {
  if (score <= 19) return "scoreTextRollover";
  if (score <= 39) return "scoreTextSlowdown";
  if (score <= 59) return "scoreTextWatch";
  if (score <= 79) return "scoreTextHealthy";
  return "scoreTextBullish";
}

function formatJobResult(job: ImportJob) {
  if (!job.result) return `${job.kind} completed.`;
  if ("sec" in job.result) {
    return `All imports finished. SEC: ${job.result.sec.successCount}/${job.result.sec.companiesProcessed} succeeded. Transcripts: ${job.result.transcripts.documentsImported} documents. RSS: ${job.result.rss.message}`;
  }
  if ("companiesProcessed" in job.result) {
    return `${job.result.source}: processed ${job.result.companiesProcessed}, ${job.result.successCount} succeeded, ${job.result.failureCount} failed, ${job.result.documentsImported} documents, ${job.result.signalsImported} signals.`;
  }
  if ("ticker" in job.result) {
    return `${job.result.ticker}: ${job.result.message} ${job.result.factsImported} facts, ${job.result.metricsImported} metrics.`;
  }
  return `${job.result.source}: ${job.result.message} ${job.result.signalsImported} signals.`;
}

function normalizeBase(base: string) {
  if (!base || base === "/") return "/";
  return `/${base.replace(/^\/+|\/+$/g, "")}/`;
}

function routeFromLocation(pathname: string) {
  if (appBase === "/") return pathname || "/";
  const baseWithoutTrailingSlash = appBase.slice(0, -1);
  if (pathname === baseWithoutTrailingSlash || pathname === appBase) return "/";
  return pathname.startsWith(appBase) ? `/${pathname.slice(appBase.length)}` : pathname;
}

function toAppPath(route: string) {
  if (appBase === "/") return route;
  return route === "/" ? appBase : `${appBase.slice(0, -1)}${route}`;
}
