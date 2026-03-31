import type { AgentResponse } from '../models/types';

interface ObservabilityTraceProps {
  response: AgentResponse;
}

export function ObservabilityTrace({ response }: ObservabilityTraceProps) {
  const pct = Math.round(response.confidence * 100);

  let confColor = 'text-emerald-600';
  if (pct < 50) confColor = 'text-red-600';
  else if (pct < 75) confColor = 'text-amber-600';

  return (
    <div className="animate-fade-in rounded-xl border border-slate-200 bg-white shadow-sm">
      <div className="flex items-center gap-2 border-b border-slate-100 px-4 py-2.5">
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-4 w-4 text-slate-400">
          <path d="M15.5 2A1.5 1.5 0 0 0 14 3.5v13a1.5 1.5 0 0 0 1.5 1.5h1a1.5 1.5 0 0 0 1.5-1.5v-13A1.5 1.5 0 0 0 16.5 2h-1ZM9.5 6A1.5 1.5 0 0 0 8 7.5v9A1.5 1.5 0 0 0 9.5 18h1a1.5 1.5 0 0 0 1.5-1.5v-9A1.5 1.5 0 0 0 10.5 6h-1ZM3.5 10A1.5 1.5 0 0 0 2 11.5v5A1.5 1.5 0 0 0 3.5 18h1A1.5 1.5 0 0 0 6 16.5v-5A1.5 1.5 0 0 0 4.5 10h-1Z" />
        </svg>
        <h3 className="text-xs font-semibold text-slate-700">Trace Summary</h3>
      </div>
      <div className="p-4 space-y-3">
        {/* Stats grid */}
        <div className="grid grid-cols-2 gap-3">
          <Stat label="Duration" value={`${response.durationMs}ms`} />
          <Stat label="Confidence" value={`${pct}%`} valueClass={confColor} />
          <Stat label="Steps" value={String(response.events.length)} />
          <Stat label="Tools" value={String(response.toolExecutions.length)} />
        </div>

        {/* Routing decision */}
        {response.routingDecision && (
          <Section title="Routing">
            <p className="text-[11px] text-slate-600">{response.routingDecision}</p>
            <div className="mt-1 flex flex-wrap gap-1">
              <RoutingBadge active={response.toolExecutions.length > 0} label="Tools" />
              <RoutingBadge active={response.ragUsed} label="RAG" />
              <RoutingBadge active={response.memoryUsed} label="Memory" />
            </div>
          </Section>
        )}

        <Section title="Agentic RAG (tool loop)">
          <p className="text-[11px] leading-relaxed text-slate-600">
            The router sets <strong>needs RAG / tools</strong> hints; the agentic loop then <strong>chooses</strong> when
            to call <code className="rounded bg-slate-100 px-0.5">search_policy_knowledge</code> vs flight/weather tools
            vs <code className="rounded bg-slate-100 px-0.5">final_answer</code>.
          </p>
          <ul className="mt-2 space-y-1 text-[11px] text-slate-700">
            <li>
              <span className="font-medium">Loop used:</span>{' '}
              {response.agenticLoopUsed ? 'yes' : 'no (legacy path or disabled)'}
            </li>
            {response.agenticIterations != null && (
              <li>
                <span className="font-medium">Iterations:</span> {response.agenticIterations}
              </li>
            )}
            {response.agenticPolicyRetrievalCount != null && (
              <li>
                <span className="font-medium">Policy retrievals:</span> {response.agenticPolicyRetrievalCount}
              </li>
            )}
            {response.agenticStopReason && (
              <li>
                <span className="font-medium">Stop reason:</span>{' '}
                <span className="font-mono text-slate-600">{response.agenticStopReason}</span>
              </li>
            )}
            {response.agenticPolicyGroundingFallback && (
              <li className="text-amber-700">Policy grounding fallback applied (KB insufficient vs router expectation).</li>
            )}
          </ul>
        </Section>

        {/* Data Sources */}
        {response.dataSources.length > 0 && (
          <Section title="Data Sources">
            <div className="flex flex-wrap gap-1">
              {response.dataSources.map((ds, i) => {
                const isMock = ds.toLowerCase().includes('mock');
                const isRag = ds.toLowerCase().includes('knowledge') || ds.toLowerCase().includes('policy');
                let style = 'bg-emerald-50 text-emerald-700 ring-emerald-600/20';
                if (isMock) style = 'bg-amber-50 text-amber-700 ring-amber-600/20';
                else if (isRag) style = 'bg-violet-50 text-violet-700 ring-violet-600/20';
                return (
                  <span key={i} className={`inline-flex items-center rounded-md px-2 py-0.5 text-[10px] font-medium ring-1 ring-inset ${style}`}>
                    {ds}
                  </span>
                );
              })}
            </div>
          </Section>
        )}

        {/* Trace ID */}
        {response.traceId && (
          <Section title="Trace ID">
            <div className="truncate font-mono text-[11px] text-slate-500">{response.traceId}</div>
          </Section>
        )}
      </div>
    </div>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="border-t border-slate-100 pt-3">
      <div className="mb-1.5 text-[10px] font-medium uppercase tracking-wide text-slate-400">{title}</div>
      {children}
    </div>
  );
}

function Stat({ label, value, valueClass = 'text-slate-900' }: { label: string; value: string; valueClass?: string }) {
  return (
    <div>
      <div className="text-[10px] font-medium uppercase tracking-wide text-slate-400">{label}</div>
      <div className={`text-lg font-bold ${valueClass}`}>{value}</div>
    </div>
  );
}

function RoutingBadge({ active, label }: { active: boolean; label: string }) {
  return (
    <span className={`inline-flex items-center gap-1 rounded-md px-2 py-0.5 text-[10px] font-semibold ring-1 ring-inset ${
      active
        ? 'bg-emerald-50 text-emerald-700 ring-emerald-600/20'
        : 'bg-slate-50 text-slate-400 ring-slate-200'
    }`}>
      <span className={`inline-block h-1.5 w-1.5 rounded-full ${active ? 'bg-emerald-500' : 'bg-slate-300'}`} />
      {label}
    </span>
  );
}
