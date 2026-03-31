import type { AgentEvent } from '../models/types';

interface LiveAgentActivityProps {
  events: AgentEvent[];
  isStreaming: boolean;
}

const STEP_META: Record<string, { icon: string; color: string }> = {
  ScopeCheck:      { icon: '🔍', color: 'text-blue-600' },
  Routing:         { icon: '🧭', color: 'text-cyan-600' },
  Planning:        { icon: '📋', color: 'text-indigo-600' },
  ToolCall:        { icon: '🔧', color: 'text-amber-600' },
  Rag:             { icon: '📚', color: 'text-violet-600' },
  Memory:          { icon: '🧠', color: 'text-pink-600' },
  Verification:    { icon: '✅', color: 'text-emerald-600' },
  SelfCorrection:  { icon: '🔄', color: 'text-purple-600' },
  Guardrail:       { icon: '🛡️', color: 'text-slate-600' },
  FinalAnswer:     { icon: '✨', color: 'text-emerald-600' },
};

function getStatusStyle(title: string | undefined): string {
  const t = (title ?? '').toLowerCase();
  if (t.includes('fail') || t.includes('error') || t.includes('rejected'))
    return 'border-l-red-400 bg-red-50/50';
  if (t.includes('fallback'))
    return 'border-l-amber-400 bg-amber-50/50';
  if (t.includes('success') || t.includes('ready') || t.includes('passed') || t.includes('complete') || t.includes('found') || t.includes('loaded'))
    return 'border-l-emerald-400 bg-emerald-50/30';
  if (t.includes('no ') || t.includes('default'))
    return 'border-l-slate-300 bg-slate-50/30';
  return 'border-l-slate-300 bg-white';
}

export function LiveAgentActivity({ events, isStreaming }: LiveAgentActivityProps) {
  if (events.length === 0 && !isStreaming) return null;

  return (
    <div className="animate-slide-up rounded-xl border border-slate-200 bg-white shadow-sm">
      <div className="flex items-center justify-between border-b border-slate-100 px-5 py-3">
        <h3 className="flex items-center gap-2 text-sm font-semibold text-slate-800">
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-4 w-4 text-slate-400">
            <path fillRule="evenodd" d="M10 18a8 8 0 1 0 0-16 8 8 0 0 0 0 16Zm.75-13a.75.75 0 0 0-1.5 0v5c0 .414.336.75.75.75h4a.75.75 0 0 0 0-1.5h-3.25V5Z" clipRule="evenodd" />
          </svg>
          Agent Activity
          {isStreaming && (
            <span className="relative ml-1 flex h-2 w-2">
              <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-75" />
              <span className="relative inline-flex h-2 w-2 rounded-full bg-emerald-500" />
            </span>
          )}
        </h3>
        <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
          {events.length} {events.length === 1 ? 'step' : 'steps'}
        </span>
      </div>
      <div className="max-h-72 overflow-y-auto p-3">
        <div className="space-y-1">
          {events.filter(e => e != null).map((event, i) => {
            const meta = STEP_META[event.stepType] || { icon: '▪️', color: 'text-slate-500' };
            return (
              <div
                key={i}
                className={`animate-fade-in flex items-start gap-2.5 rounded-lg border-l-[3px] px-3 py-2 text-sm ${getStatusStyle(event.title)}`}
              >
                <span className="mt-0.5 shrink-0 text-sm">{meta.icon}</span>
                <div className="min-w-0 flex-1">
                  <div className="flex items-baseline gap-2">
                    <span className={`font-medium ${meta.color}`}>{event.title ?? 'Processing'}</span>
                    <span className="shrink-0 text-[10px] text-slate-400">
                      {event.timestamp ? new Date(event.timestamp).toLocaleTimeString() : ''}
                    </span>
                  </div>
                  {event.content && (
                    <p className="mt-0.5 truncate text-xs text-slate-500">{event.content}</p>
                  )}
                </div>
              </div>
            );
          })}
          {isStreaming && events.length === 0 && (
            <div className="flex items-center gap-2 px-3 py-4 text-sm text-slate-400">
              <svg className="h-4 w-4 animate-spin text-primary-500" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
              </svg>
              Initializing agent pipeline...
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
