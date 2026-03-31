interface AgentPlanProps {
  plan: string[];
  isStreaming: boolean;
}

export function AgentPlan({ plan, isStreaming }: AgentPlanProps) {
  if (plan.length === 0) return null;

  return (
    <div className="animate-slide-up rounded-xl border border-indigo-200 bg-gradient-to-br from-indigo-50/80 to-white shadow-sm">
      <div className="flex items-center gap-2 border-b border-indigo-100 px-5 py-3">
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-4 w-4 text-indigo-500">
          <path fillRule="evenodd" d="M6 4.75A.75.75 0 0 1 6.75 4h10.5a.75.75 0 0 1 0 1.5H6.75A.75.75 0 0 1 6 4.75ZM6 10a.75.75 0 0 1 .75-.75h10.5a.75.75 0 0 1 0 1.5H6.75A.75.75 0 0 1 6 10Zm0 5.25a.75.75 0 0 1 .75-.75h10.5a.75.75 0 0 1 0 1.5H6.75a.75.75 0 0 1-.75-.75ZM1.99 4.75a1 1 0 0 1 1-1H3a1 1 0 0 1 1 1v.01a1 1 0 0 1-1 1h-.01a1 1 0 0 1-1-1v-.01ZM1.99 15.25a1 1 0 0 1 1-1H3a1 1 0 0 1 1 1v.01a1 1 0 0 1-1 1h-.01a1 1 0 0 1-1-1v-.01ZM1.99 10a1 1 0 0 1 1-1H3a1 1 0 0 1 1 1v.01a1 1 0 0 1-1 1h-.01a1 1 0 0 1-1-1V10Z" clipRule="evenodd" />
        </svg>
        <h3 className="text-sm font-semibold text-indigo-800">Agent Plan</h3>
      </div>
      <div className="p-5">
        <ol className="space-y-2">
          {plan.map((step, i) => (
            <li key={i} className="flex items-start gap-3">
              <span className="mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-indigo-100 text-[10px] font-bold text-indigo-700">
                {i + 1}
              </span>
              <span className="text-sm leading-relaxed text-slate-700">{step}</span>
            </li>
          ))}
        </ol>
        {isStreaming && (
          <div className="mt-3 flex items-center gap-2 text-xs text-indigo-400">
            <svg className="h-3 w-3 animate-spin" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
              <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
              <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
            </svg>
            Executing plan...
          </div>
        )}
      </div>
    </div>
  );
}
