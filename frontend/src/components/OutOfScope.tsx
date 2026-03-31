interface OutOfScopeProps {
  intent: string;
  summary: string;
}

export function OutOfScope({ intent, summary }: OutOfScopeProps) {
  return (
    <div className="animate-slide-up rounded-xl border border-slate-300 bg-gradient-to-br from-slate-50 to-white shadow-sm">
      <div className="p-6 text-center">
        <div className="mx-auto mb-3 flex h-12 w-12 items-center justify-center rounded-full bg-slate-100">
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-6 w-6 text-slate-400">
            <path fillRule="evenodd" d="M8.485 2.495c.673-1.167 2.357-1.167 3.03 0l6.28 10.875c.673 1.167-.17 2.625-1.516 2.625H3.72c-1.347 0-2.189-1.458-1.515-2.625L8.485 2.495ZM10 5a.75.75 0 0 1 .75.75v3.5a.75.75 0 0 1-1.5 0v-3.5A.75.75 0 0 1 10 5Zm0 9a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z" clipRule="evenodd" />
          </svg>
        </div>
        <span className="mb-2 inline-block rounded-full bg-slate-200 px-3 py-1 text-xs font-semibold uppercase tracking-wider text-slate-600">
          Out of Scope
        </span>
        <p className="mt-2 text-sm text-slate-600">
          {summary || 'This request is outside the scope of the Travel Disruption Agent.'}
        </p>
        {intent && (
          <p className="mt-1 text-xs text-slate-400">
            Detected intent: <span className="font-medium">{intent}</span>
          </p>
        )}
        <p className="mt-4 text-xs text-slate-400">
          Try asking about flight disruptions, weather impacts, rebooking options, or travel alternatives.
        </p>
      </div>
    </div>
  );
}
