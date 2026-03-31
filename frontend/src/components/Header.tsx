export function Header() {
  return (
    <header className="border-b border-slate-200 bg-white">
      <div className="mx-auto flex max-w-7xl items-center justify-between px-4 py-3 sm:px-6 lg:px-8">
        <div className="flex items-center gap-3">
          <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-primary-600 text-lg text-white">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-5 w-5">
              <path d="M3.105 2.288a.75.75 0 0 0-.826.95l1.414 4.926A1.5 1.5 0 0 0 5.135 9.25h6.115a.75.75 0 0 1 0 1.5H5.135a1.5 1.5 0 0 0-1.442 1.086l-1.414 4.926a.75.75 0 0 0 .826.95 28.897 28.897 0 0 0 15.293-7.155.75.75 0 0 0 0-1.114A28.897 28.897 0 0 0 3.105 2.288Z" />
            </svg>
          </div>
          <div>
            <h1 className="text-base font-semibold text-slate-900">
              Travel Disruption Agent
            </h1>
            <p className="text-xs text-slate-500">
              AI-powered travel disruption management
            </p>
          </div>
        </div>
        <div className="flex items-center gap-1.5 text-xs text-slate-500">
          <span className="inline-block h-1.5 w-1.5 rounded-full bg-emerald-500" />
          System Ready
        </div>
      </div>
    </header>
  );
}
