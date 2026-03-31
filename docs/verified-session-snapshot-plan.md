# Plan: structured verified session snapshot (replace regex on assistant text)

## Current behavior

- After each turn, the API persists `user` and `assistant` messages in `IMemoryService`.
- Some follow-up behavior (for example `conversation_history_fallback`) recovers flight/route facts by **regex-parsing prior assistant prose**. That is brittle (format drift, localization) and mixes “display text” with “machine-readable evidence.”

## Target behavior

1. **After each successful turn** (or after tool execution, before final recommendation), persist a **small structured JSON snapshot** alongside the conversation, keyed by the same session storage key and turn id (or monotonic sequence).
2. The snapshot should contain only **fields we already trust**: normalized tool outputs, primary flight numbers, route pairs, scheduled times, data source labels — not free-form model monologue.
3. **Follow-up routing and fallbacks** read the snapshot (or the last snapshot) instead of scanning assistant markdown.
4. **Trust boundary** stays explicit: snapshots are **written only by the server** after validation/guardrail steps; clients never supply snapshot bodies on chat requests.

## Migration

1. Define a versioned DTO (e.g. `SessionTurnSnapshot { int Sequence; string SchemaVersion; ... }`).
2. Store in the same backing store as sessions (Mongo document sub-array or side collection).
3. Implement snapshot build from existing `ToolExecutionResult` + `VerifiedContext` pipeline.
4. Switch `ConversationHistoryRouteFallback` to prefer snapshot data when present; keep regex path behind a compatibility flag for one release if needed.
5. Remove regex path once snapshots cover production traffic.

## Testing

- Unit tests: snapshot serialization round-trip; fallback selection when snapshot exists vs missing.
- Integration: two-turn flow asserts behavior using snapshot only (no matching assistant regex).
