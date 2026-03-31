interface LlmErrorBannerProps {
  errors: string[];
}

export function LlmErrorBanner({ errors }: LlmErrorBannerProps) {
  if (errors.length === 0) return null;

  return (
    <div className="animate-slide-up rounded-xl border border-red-200 bg-gradient-to-br from-red-50/80 to-orange-50/50 shadow-sm">
      <div className="flex items-center gap-2.5 border-b border-red-100 px-5 py-3">
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-5 w-5 shrink-0 text-red-500">
          <path fillRule="evenodd" d="M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Zm-8-5a.75.75 0 0 1 .75.75v4.5a.75.75 0 0 1-1.5 0v-4.5A.75.75 0 0 1 10 5Zm0 10a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z" clipRule="evenodd" />
        </svg>
        <h3 className="text-sm font-semibold text-red-800">
          AI Engine Unavailable
        </h3>
      </div>
      <div className="space-y-3 p-5">
        <p className="text-sm leading-relaxed text-slate-700">
          The LLM-powered <strong>summary step</strong> failed (e.g. rate limit 429 or quota).
          This is <strong>not</strong> the same as “no flights found” — flight and weather
          tools usually completed first. The data in <strong>Flight Data</strong> and{' '}
          <strong>Weather Conditions</strong> below is from live APIs; only the final AI
          wording could not be generated.
        </p>

        <div className="rounded-lg border border-red-100 bg-white/60 p-3">
          <div className="mb-1.5 text-[10px] font-semibold uppercase tracking-wide text-red-400">
            Errors
          </div>
          <ul className="space-y-1">
            {errors.map((err, i) => (
              <li key={i} className="flex items-start gap-2 text-xs text-red-700">
                <span className="mt-0.5 shrink-0 font-bold">&times;</span>
                <span>{err}</span>
              </li>
            ))}
          </ul>
        </div>

        <div className="rounded-lg border border-amber-100 bg-amber-50/60 px-3 py-2">
          <p className="text-xs leading-relaxed text-amber-800">
            <strong>Note:</strong> A template-based fallback response is
            available, but we chose not to use it — we prefer to show you
            the real system status. To enable AI recommendations, verify your
            API key and permissions in the application settings.
          </p>
        </div>
      </div>
    </div>
  );
}
