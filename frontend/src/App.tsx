import { AlertTriangle, BarChart3, Building2, FileText, Gauge, PlusCircle, RadioTower } from "lucide-react";
import { FormEvent, useEffect, useState } from "react";
import { Alert, api, CategoryStatus, Company, CompanyDetail, DashboardSummary, ManualEntry, QuarterScore, TranscriptSignal } from "./api";

const nav = [
  ["/", "Dashboard", Gauge],
  ["/indicators", "Indicators", BarChart3],
  ["/transcripts", "Transcripts", FileText],
  ["/risk-history", "History", RadioTower],
  ["/alerts", "Alerts", AlertTriangle],
  ["/manual-entry", "Manual Entry", PlusCircle]
] as const;

export function App() {
  const [path, setPath] = useState(window.location.pathname);

  useEffect(() => {
    const onPop = () => setPath(window.location.pathname);
    window.addEventListener("popstate", onPop);
    return () => window.removeEventListener("popstate", onPop);
  }, []);

  const go = (to: string) => {
    window.history.pushState(null, "", to);
    setPath(to);
  };

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
        {path === "/" && <Dashboard go={go} />}
        {path.startsWith("/companies/") && <CompanyPage ticker={path.split("/").pop() ?? "MSFT"} />}
        {path === "/indicators" && <IndicatorsPage />}
        {path === "/transcripts" && <TranscriptsPage />}
        {path === "/risk-history" && <HistoryPage />}
        {path === "/alerts" && <AlertsPage />}
        {path === "/manual-entry" && <ManualEntryPage />}
      </main>
    </div>
  );
}

function Dashboard({ go }: { go: (path: string) => void }) {
  const summary = useApi(api.summary);
  const companies = useApi(api.companies);
  if (!summary.data || !companies.data) return <Loading error={summary.error ?? companies.error} />;
  const data = summary.data;

  return (
    <>
      <Header title="Overall AI CapEx Risk Dashboard" subtitle="Seeded public-signal monitor for AI infrastructure momentum." />
      <section className="scoreBand">
        <div className="scoreDial">
          <span>{data.currentRiskScore}</span>
          <small>{data.riskBand}</small>
        </div>
        <MetricCard label="Change vs previous quarter" value={`${signed(data.changeVsPreviousQuarter)} pts`} />
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
            <strong className={item.averageSignal > 20 ? "bad" : item.averageSignal < -10 ? "good" : ""}>{item.status}</strong>
            <p>{item.summary}</p>
          </article>
        ))}
      </section>
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

function CompanyPage({ ticker }: { ticker: string }) {
  const detail = useApi(() => api.company(ticker), [ticker]);
  if (!detail.data) return <Loading error={detail.error} />;
  const { company, metrics, signals, sources } = detail.data;
  return (
    <>
      <Header title={`${company.ticker} - ${company.name}`} subtitle={company.segment} />
      <section className="grid three">
        <MetricCard label="Latest risk signal" value={company.latestRiskSignal.toFixed(1)} />
        <MetricCard label="Signal count" value={signals.length.toString()} />
        <MetricCard label="Source docs" value={sources.length.toString()} />
      </section>
      <DataTable title="Financial metrics" rows={metrics.map((m) => [m.quarter, prettyMetric(m.kind), `${m.value} ${m.unit}`])} headers={["Quarter", "Metric", "Value"]} />
      <SignalList title="Company signals" signals={signals} />
      <DataTable title="Sources" rows={sources.map((s) => [s.sourceType, s.title, s.summary])} headers={["Type", "Title", "Summary"]} />
    </>
  );
}

function IndicatorsPage() {
  const indicators = useApi(api.indicators);
  if (!indicators.data) return <Loading error={indicators.error} />;
  return (
    <>
      <Header title="Indicator Trend Page" subtitle="Weighted categories behind the current slowdown risk score." />
      <div className="grid two">
        {indicators.data.map((item: CategoryStatus) => (
          <article className="panel" key={item.category}>
            <h2>{labelCategory(item.category)}</h2>
            <div className="bar"><span style={{ width: `${Math.min(100, Math.abs(item.averageSignal))}%` }} /></div>
            <p><strong>{item.status}</strong> ({item.averageSignal})</p>
            <p>{item.summary}</p>
          </article>
        ))}
      </div>
    </>
  );
}

function TranscriptsPage() {
  const transcripts = useApi(api.transcripts);
  if (!transcripts.data) return <Loading error={transcripts.error} />;
  return (
    <>
      <Header title="Transcript Signal Explorer" subtitle="Keyword-based mentions from seeded earnings-call text." />
      <DataTable title="Transcript mentions" rows={transcripts.data.map((t: TranscriptSignal) => [t.ticker, t.quarter, t.keywordGroup, t.count.toString(), t.title])} headers={["Ticker", "Quarter", "Group", "Count", "Transcript"]} />
    </>
  );
}

function HistoryPage() {
  const history = useApi(api.history);
  if (!history.data) return <Loading error={history.error} />;
  return (
    <>
      <Header title="Risk Score History" subtitle="Quarterly score snapshots generated from seeded category signals." />
      <section className="panel chart">
        {history.data.map((point: QuarterScore) => (
          <div className="column" key={point.quarter}>
            <span className={scoreClass(point.score)} style={{ height: `${Math.max(point.score * 2, 12)}px` }} />
            <strong>{point.score}</strong>
            <small>{point.quarter}</small>
          </div>
        ))}
      </section>
      <DataTable title="History" rows={history.data.map((h) => [h.quarter, h.score.toString(), signed(h.change), h.band])} headers={["Quarter", "Score", "Change", "Band"]} />
    </>
  );
}

function AlertsPage() {
  const alerts = useApi(api.alerts);
  if (!alerts.data) return <Loading error={alerts.error} />;
  return (
    <>
      <Header title="Alerts" subtitle="Watchlist conditions triggered by the current seeded dataset." />
      <div className="stack">
        {alerts.data.map((alert: Alert) => (
          <article className={`panel alert ${alert.severity.toLowerCase()}`} key={alert.id}>
            <strong>{alert.title}</strong>
            <span>{alert.severity}</span>
            <p>{alert.message}</p>
          </article>
        ))}
      </div>
    </>
  );
}

function ManualEntryPage() {
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
  };

  return (
    <>
      <Header title="Manual Data Entry" subtitle="Add analyst-observed indicator signals to the current quarter." />
      <form className="panel form" onSubmit={submit}>
        <label>Ticker<input name="ticker" defaultValue="MSFT" required /></label>
        <label>Category<select name="category" defaultValue="HyperscalerCapexRevisionTrend">{categoryOptions.map((x) => <option key={x} value={x}>{labelCategory(x)}</option>)}</select></label>
        <label>Score impact<input name="scoreImpact" type="number" min="-100" max="100" defaultValue="20" required /></label>
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

function MetricCard({ label, value, text = false }: { label: string; value: string; text?: boolean }) {
  return <article className="metric"><span>{label}</span><strong className={text ? "textValue" : ""}>{value}</strong></article>;
}

function SignalList({ title, signals }: { title: string; signals: DashboardSummary["topPositiveIndicators"] }) {
  return <section className="panel"><h2>{title}</h2><div className="stack">{signals.map((s) => <article className="signal" key={`${s.name}-${s.ticker}`}><strong>{s.name}</strong><span className={s.direction.toLowerCase()}>{s.direction} {signed(s.scoreImpact)}</span><p>{s.summary}</p></article>)}</div></section>;
}

function DataTable({ title, headers, rows }: { title: string; headers: string[]; rows: string[][] }) {
  return <section className="panel"><h2>{title}</h2><div className="tableWrap"><table><thead><tr>{headers.map((h) => <th key={h}>{h}</th>)}</tr></thead><tbody>{rows.map((row, index) => <tr key={index}>{row.map((cell, i) => <td key={i}>{cell}</td>)}</tr>)}</tbody></table></div></section>;
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
  if (score <= 25) return "scoreBullish";
  if (score <= 45) return "scoreHealthy";
  if (score <= 60) return "scoreWatch";
  if (score <= 75) return "scoreSlowdown";
  return "scoreRollover";
}
