import type { ToolExecutionResult } from '../models/types';

interface ToolCallsProps {
  tools: ToolExecutionResult[];
}

function DataSourceBadge({ source }: { source: string }) {
  const isMock = source.toLowerCase().includes('mock');
  const isFallback = source.toLowerCase().includes('fallback');

  let style = 'bg-emerald-50 text-emerald-700 ring-emerald-600/20';
  let label = source;

  if (isMock) {
    style = 'bg-amber-50 text-amber-700 ring-amber-600/20';
    label = source;
  } else if (isFallback) {
    style = 'bg-orange-50 text-orange-700 ring-orange-600/20';
    label = source;
  }

  return (
    <span className={`inline-flex items-center rounded-md px-2 py-0.5 text-[10px] font-semibold ring-1 ring-inset ${style}`}>
      {label}
    </span>
  );
}

function StatusBadge({ success }: { success: boolean }) {
  return success ? (
    <span className="inline-flex items-center gap-1 rounded-md bg-emerald-50 px-2 py-0.5 text-[10px] font-semibold text-emerald-700 ring-1 ring-inset ring-emerald-600/20">
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" className="h-3 w-3">
        <path fillRule="evenodd" d="M12.416 3.376a.75.75 0 0 1 .208 1.04l-5 7.5a.75.75 0 0 1-1.154.114l-3-3a.75.75 0 0 1 1.06-1.06l2.353 2.353 4.493-6.74a.75.75 0 0 1 1.04-.207Z" clipRule="evenodd" />
      </svg>
      Success
    </span>
  ) : (
    <span className="inline-flex items-center gap-1 rounded-md bg-red-50 px-2 py-0.5 text-[10px] font-semibold text-red-700 ring-1 ring-inset ring-red-600/20">
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" className="h-3 w-3">
        <path d="M5.28 4.22a.75.75 0 0 0-1.06 1.06L6.94 8l-2.72 2.72a.75.75 0 1 0 1.06 1.06L8 9.06l2.72 2.72a.75.75 0 1 0 1.06-1.06L9.06 8l2.72-2.72a.75.75 0 0 0-1.06-1.06L8 6.94 5.28 4.22Z" />
      </svg>
      Failed
    </span>
  );
}

function ToolCard({ tool }: { tool: ToolExecutionResult }) {
  return (
    <div className="rounded-lg p-3">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-sm font-medium text-slate-800">{formatToolName(tool.toolName)}</span>
        <StatusBadge success={tool.success} />
        <DataSourceBadge source={tool.dataSource} />
        <span className="ml-auto text-[10px] text-slate-400">{tool.durationMs}ms</span>
      </div>
      <div className="mt-1.5 text-xs text-slate-500">
        <span className="font-medium text-slate-600">Input:</span> {tool.input}
      </div>
      {tool.success && (
        <div className="mt-1 text-xs text-slate-600">
          <span className="font-medium">Result:</span>{' '}
          <span className="whitespace-pre-wrap">{tool.output}</span>
        </div>
      )}
      {tool.errorMessage && (
        <div className="mt-1.5 rounded-md bg-red-50 px-2 py-1 text-xs text-red-600">
          {tool.errorMessage}
        </div>
      )}
      {tool.warning && (
        <div className="mt-1.5 rounded-md bg-amber-50 px-2 py-1 text-xs text-amber-700">
          {tool.warning}
        </div>
      )}
    </div>
  );
}

interface SectionProps {
  icon: React.ReactNode;
  title: string;
  count: number;
  borderColor: string;
  bgGradient: string;
  headerBorder: string;
  badgeBg: string;
  badgeText: string;
  titleColor: string;
  dividerColor: string;
  tools: ToolExecutionResult[];
}

function ToolSection({ icon, title, count, borderColor, bgGradient, headerBorder, badgeBg, badgeText, titleColor, dividerColor, tools }: SectionProps) {
  if (tools.length === 0) return null;
  return (
    <div className={`animate-slide-up rounded-xl border ${borderColor} bg-gradient-to-br ${bgGradient} shadow-sm`}>
      <div className={`flex items-center justify-between border-b ${headerBorder} px-5 py-3`}>
        <div className="flex items-center gap-2">
          {icon}
          <h3 className={`text-sm font-semibold ${titleColor}`}>{title}</h3>
        </div>
        <span className={`rounded-full ${badgeBg} px-2 py-0.5 text-xs font-medium ${badgeText}`}>
          {count} {count === 1 ? 'query' : 'queries'}
        </span>
      </div>
      <div className={`divide-y ${dividerColor} p-2`}>
        {tools.map((tool, i) => <ToolCard key={i} tool={tool} />)}
      </div>
    </div>
  );
}

export function ToolCalls({ tools }: ToolCallsProps) {
  if (tools.length === 0) return null;

  const flightTools = tools.filter(t => t.toolName.toLowerCase().includes('flight'));
  const weatherTools = tools.filter(t => t.toolName.toLowerCase().includes('weather'));
  const otherTools = tools.filter(t =>
    !t.toolName.toLowerCase().includes('flight') &&
    !t.toolName.toLowerCase().includes('weather')
  );

  return (
    <div className="space-y-4">
      <ToolSection
        icon={
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-4 w-4 text-blue-500">
            <path d="M3.105 2.288a.75.75 0 0 0-.826.95l1.414 4.926A1.5 1.5 0 0 0 5.135 9.25h6.115a.75.75 0 0 1 0 1.5H5.135a1.5 1.5 0 0 0-1.442 1.086l-1.414 4.926a.75.75 0 0 0 .826.95 28.897 28.897 0 0 0 15.293-7.155.75.75 0 0 0 0-1.114A28.897 28.897 0 0 0 3.105 2.288Z" />
          </svg>
        }
        title="Flight Data"
        count={flightTools.length}
        borderColor="border-blue-200"
        bgGradient="from-blue-50/60 to-white"
        headerBorder="border-blue-100"
        badgeBg="bg-blue-100"
        badgeText="text-blue-700"
        titleColor="text-blue-800"
        dividerColor="divide-blue-100/80"
        tools={flightTools}
      />

      <ToolSection
        icon={
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-4 w-4 text-sky-500">
            <path fillRule="evenodd" d="M4.5 2A1.5 1.5 0 0 0 3 3.5v13A1.5 1.5 0 0 0 4.5 18h11a1.5 1.5 0 0 0 1.5-1.5V7.621a1.5 1.5 0 0 0-.44-1.06l-4.12-4.122A1.5 1.5 0 0 0 11.378 2H4.5Zm4.75 6.75a.75.75 0 0 0-1.5 0v2.546l-.943-1.048a.75.75 0 1 0-1.114 1.004l2.25 2.5a.75.75 0 0 0 1.114 0l2.25-2.5a.75.75 0 1 0-1.114-1.004l-.943 1.048V8.75Z" clipRule="evenodd" />
          </svg>
        }
        title="Weather Conditions"
        count={weatherTools.length}
        borderColor="border-sky-200"
        bgGradient="from-sky-50/60 to-white"
        headerBorder="border-sky-100"
        badgeBg="bg-sky-100"
        badgeText="text-sky-700"
        titleColor="text-sky-800"
        dividerColor="divide-sky-100/80"
        tools={weatherTools}
      />

      {otherTools.length > 0 && (
        <ToolSection
          icon={
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-4 w-4 text-amber-500">
              <path fillRule="evenodd" d="M14.5 10a4.5 4.5 0 0 0 4.284-5.882c-.105-.324-.51-.391-.752-.15L15.34 6.66a.454.454 0 0 1-.493.11 3.01 3.01 0 0 1-1.618-1.616.455.455 0 0 1 .11-.494l2.694-2.692c.24-.241.174-.647-.15-.752a4.5 4.5 0 0 0-5.873 4.575c.055.873-.128 1.808-.8 2.368l-7.23 6.024a2.724 2.724 0 1 0 3.837 3.837l6.024-7.23c.56-.672 1.495-.855 2.368-.8.096.007.193.01.291.01ZM5 16a1 1 0 1 1-2 0 1 1 0 0 1 2 0Z" clipRule="evenodd" />
            </svg>
          }
          title="Additional Data"
          count={otherTools.length}
          borderColor="border-amber-200"
          bgGradient="from-amber-50/60 to-white"
          headerBorder="border-amber-100"
          badgeBg="bg-amber-100"
          badgeText="text-amber-700"
          titleColor="text-amber-800"
          dividerColor="divide-amber-100/80"
          tools={otherTools}
        />
      )}
    </div>
  );
}

function formatToolName(name: string): string {
  return name
    .replace(/([A-Z])/g, ' $1')
    .replace(/^\s/, '')
    .replace('Tool', '')
    .trim();
}
