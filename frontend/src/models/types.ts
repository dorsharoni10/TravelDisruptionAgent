export type AgentStepType =
  | 'ScopeCheck'
  | 'Routing'
  | 'AgentReasoning'
  | 'Planning'
  | 'ToolCall'
  | 'Rag'
  | 'Memory'
  | 'Verification'
  | 'SelfCorrection'
  | 'Guardrail'
  | 'FinalAnswer';

export interface AgentEvent {
  stepType: AgentStepType;
  title: string;
  content: string;
  timestamp: string;
}

/** SSE `event: stream_error` payload from POST /api/chat/stream. */
export interface StreamErrorSse {
  code: string;
  message: string;
}

export interface ToolExecutionResult {
  toolName: string;
  input: string;
  output: string;
  success: boolean;
  dataSource: string;
  durationMs: number;
  errorMessage?: string;
  warning?: string;
}

export interface SelfCorrectionStep {
  issue: string;
  action: string;
  outcome: string;
  timestamp: string;
}

export interface AgentLoopIterationRecord {
  index: number;
  thought: string;
  knownSummary: string;
  stillMissing: string;
  action: string;
  capability: string;
  argumentsSummary: string;
  observationSummary: string;
  retrievalBestSimilarity?: number;
  retrievalChunkCount: number;
}

export interface AgentResponse {
  /** Echoed session id for multi-turn; send as sessionId on the next request. */
  sessionId?: string;
  /** Server transcript after this turn (source of truth for the UI). */
  conversationHistory?: { role: string; content: string; timestampUtc?: string }[];
  inScope: boolean;
  intent: string;
  /** Workflow intent for planning/validation (e.g. flight_cancellation). */
  workflowIntent?: string;
  summary: string;
  agentPlan: string[];
  toolExecutions: ToolExecutionResult[];
  selfCorrectionSteps: SelfCorrectionStep[];
  finalRecommendation: string;
  warnings: string[];
  confidence: number;
  dataSources: string[];
  traceId: string;
  durationMs: number;
  events: AgentEvent[];
  memoryUsed: boolean;
  ragUsed: boolean;
  routingDecision: string;
  ragContext: string[];
  llmErrors: string[];
  agenticLoopUsed?: boolean;
  agenticStopReason?: string;
  agenticIterations?: number;
  agenticPolicyRetrievalCount?: number;
  agenticPolicyQueries?: string[];
  agenticTrace?: AgentLoopIterationRecord[];
  answerGroundedOnPolicy?: boolean;
  answerGroundedOnTools?: boolean;
  agenticStructuredLlmOnly?: boolean;
  agenticPolicyGroundingFallback?: boolean;
  agenticCitationWarnings?: string[];
}

export interface ChatRequest {
  message: string;
  sessionId?: string;
  userId?: string;
}

export interface ChatHistoryMessage {
  role: 'user' | 'assistant';
  content: string;
  /** Shown while the message is not yet persisted on the server. */
  pending?: boolean;
  timestampUtc?: string;
}

export interface UserPreferences {
  userId: string;
  preferredAirline: string;
  seatPreference: string;
  mealPreference: string;
  loyaltyProgram: string;
  maxLayovers: number;
  maxBudgetUsd: number;
  preferredAirports: string[];
  homeAirport: string;
  riskTolerance: string;
  prefersRemoteFallback: boolean;
  preferredMeetingBufferMinutes: number;
  updatedAt: string;
}
