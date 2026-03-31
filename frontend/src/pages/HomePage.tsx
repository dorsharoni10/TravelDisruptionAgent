import { AgentPlan } from '../components/AgentPlan';
import { ChatHistoryPanel } from '../components/ChatHistoryPanel';
import { FinalRecommendation } from '../components/FinalRecommendation';
import { InputArea } from '../components/InputArea';
import { LiveAgentActivity } from '../components/LiveAgentActivity';
import { LlmErrorBanner } from '../components/LlmErrorBanner';
import { MemoryPreferences } from '../components/MemoryPreferences';
import { ObservabilityTrace } from '../components/ObservabilityTrace';
import { OutOfScope } from '../components/OutOfScope';
import { SelfCorrection } from '../components/SelfCorrection';
import { SuggestedPrompts } from '../components/SuggestedPrompts';
import { ToolCalls } from '../components/ToolCalls';
import { WarningsPanel } from '../components/WarningsPanel';
import { useState } from 'react';
import { useAgentStream } from '../hooks/useAgentStream';
import { usePreferences } from '../hooks/usePreferences';

export function HomePage() {
  const { events, response, displayMessages, isStreaming, error, sendMessage, reset } =
    useAgentStream();
  const { preferences, loading: prefsLoading } = usePreferences();
  const [composerText, setComposerText] = useState('');
  const [composerFocusNonce, setComposerFocusNonce] = useState(0);

  const fillComposerFromExample = (text: string) => {
    setComposerText(text);
    setComposerFocusNonce((n) => n + 1);
  };

  const hasSession =
    displayMessages.length > 0 || events.length > 0 || isStreaming || !!response;
  const isOutOfScope = response && !response.inScope;

  const plan = response?.agentPlan ?? [];
  const tools = response?.toolExecutions ?? [];
  const corrections = response?.selfCorrectionSteps ?? [];
  const warnings = response?.warnings ?? [];
  const llmErrors = response?.llmErrors ?? [];

  return (
    <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
      <ChatHistoryPanel
        messages={displayMessages}
        onClear={() => {
          void reset();
          setComposerText('');
        }}
        clearDisabled={isStreaming}
      />

      {/* Input area */}
      <div className="mb-6">
        <InputArea
          message={composerText}
          onMessageChange={setComposerText}
          onSend={(msg) => {
            void sendMessage(msg);
            setComposerText('');
          }}
          disabled={isStreaming}
          focusNonce={composerFocusNonce}
        />
      </div>

      {/* Suggested prompts (only when no active session) */}
      {!hasSession && (
        <div className="mb-6">
          <SuggestedPrompts onSelect={fillComposerFromExample} />
        </div>
      )}

      {/* Error display */}
      {error && (
        <div className="mb-6 animate-fade-in rounded-xl border border-red-200 bg-red-50 px-5 py-4 shadow-sm">
          <div className="flex items-start gap-3">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="mt-0.5 h-5 w-5 shrink-0 text-red-500">
              <path fillRule="evenodd" d="M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Zm-8-5a.75.75 0 0 1 .75.75v4.5a.75.75 0 0 1-1.5 0v-4.5A.75.75 0 0 1 10 5Zm0 10a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z" clipRule="evenodd" />
            </svg>
            <div>
              <h4 className="text-sm font-semibold text-red-800">Connection Error</h4>
              <p className="mt-0.5 text-sm text-red-600">{error}</p>
            </div>
          </div>
        </div>
      )}

      {/* Empty state */}
      {!hasSession && !error && (
        <div className="mt-12 text-center animate-fade-in">
          <div className="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-2xl bg-primary-50">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" className="h-8 w-8 text-primary-500">
              <path d="M3.478 2.404a.75.75 0 0 0-.926.941l2.432 7.905H13.5a.75.75 0 0 1 0 1.5H4.984l-2.432 7.905a.75.75 0 0 0 .926.94 60.519 60.519 0 0 0 18.445-8.986.75.75 0 0 0 0-1.218A60.517 60.517 0 0 0 3.478 2.404Z" />
            </svg>
          </div>
          <h2 className="text-lg font-semibold text-slate-800">
            Describe your travel disruption
          </h2>
          <p className="mx-auto mt-1 max-w-md text-sm text-slate-500">
            Get AI-powered recommendations for flight cancellations, weather disruptions, rebooking options, and alternative travel plans.
          </p>
        </div>
      )}

      {/* Main content grid */}
      {hasSession && (
        <div className="grid grid-cols-1 gap-5 lg:grid-cols-12">
          {/* Main panels */}
          <div className="space-y-5 lg:col-span-8">
            {/* Live activity (always shown during/after session) */}
            <LiveAgentActivity events={events} isStreaming={isStreaming} />

            {/* Out of scope */}
            {isOutOfScope && (
              <OutOfScope
                intent={response.intent}
                summary={response.summary}
              />
            )}

            {/* In-scope results */}
            {response && response.inScope && (
              <>
                <AgentPlan plan={plan} isStreaming={isStreaming} />
                <ToolCalls tools={tools} />
                <SelfCorrection steps={corrections} />
                {llmErrors.length > 0 ? (
                  <LlmErrorBanner errors={llmErrors} />
                ) : (
                  <FinalRecommendation
                    recommendation={response.finalRecommendation}
                    confidence={response.confidence}
                    intent={response.intent}
                  />
                )}
              </>
            )}
          </div>

          {/* Sidebar */}
          <div className="space-y-4 lg:col-span-4">
            {response && <ObservabilityTrace response={response} />}
            {warnings.length > 0 && <WarningsPanel warnings={warnings} />}
            <MemoryPreferences
              preferences={preferences}
              loading={prefsLoading}
              memoryUsed={response?.memoryUsed}
            />
          </div>
        </div>
      )}
    </div>
  );
}
