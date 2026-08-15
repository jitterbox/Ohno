/**
 * Ohno wire protocol — canonical TypeScript contract.
 *
 * Transport: header-delimited JSON-RPC 2.0 over stdio
 * (StreamJsonRpc on .NET <-> vscode-jsonrpc in the extension).
 *
 * The C# records in ComplexityAnalyzer.Server/Protocol/Contracts.cs
 * mirror these types; keep them in sync.
 *
 * `confidenceReasons` is empty when confidence is high. Below high it
 * lists the assumptions that cap the result (idiom match, amortized
 * catalog, unresolved call, opaque API).
 */

export type AnalysisTier = 'fast' | 'deep';

export type Confidence = 'high' | 'medium' | 'low' | 'unknown';

export type FunctionKind =
  | 'method'
  | 'constructor'
  | 'localFunction'
  | 'lambda'
  | 'property'
  | 'operator';

export interface LineRange {
  startLine: number;
  startCharacter: number;
  endLine: number;
  endCharacter: number;
}

export interface InputDimension {
  /** Symbolic variable used in expressions, e.g. "n", "m", "k". */
  variable: string;
  /** What the variable means, e.g. "items.Length" or 'parameter "k"'. */
  meaning: string;
}

export interface AnalysisWarning {
  message: string;
  range?: LineRange;
}

export interface BoundingSuggestion {
  /** Human-readable description of the opportunity. */
  description: string;
  /** The bounding condition that would enable it, e.g. "dequeue when Count > k". */
  condition: string;
  resultingTime: string;
  resultingSpace: string;
}

/**
 * A node in the derivation tree. Doubles as the UI nesting model:
 * children roll up mathematically into their parent's cost.
 */
export interface EvidenceNode {
  /** e.g. 'sequence' | 'loop' | 'conditional' | 'call' | 'linq' | 'recursion' | 'allocation'. */
  kind: string;
  /** Human-readable label, e.g. "foreach over values" or "PriorityQueue.Enqueue". */
  label: string;
  /** Formatted subtotal cost contributed by this subtree, e.g. "n * log(k)". */
  cost: string;
  /** Source range this node annotates, if any. */
  range?: LineRange;
  children: EvidenceNode[];
}

export interface FunctionComplexity {
  /** Stable identity within a document version (symbol + position). */
  id: string;
  name: string;
  kind: FunctionKind;
  /** Whole-function range. */
  range: LineRange;
  /** Signature range; inline annotations attach at its end. */
  signatureRange: LineRange;
  /** Formatted Big-O time, e.g. "O(n log k)". */
  time: string;
  /** Formatted Big-O auxiliary space, e.g. "O(k)". */
  space: string;
  confidence: Confidence;
  dimensions: InputDimension[];
  evidence: EvidenceNode;
  warnings: AnalysisWarning[];
  boundingSuggestions: BoundingSuggestion[];
  /** Plain-language gloss, or empty when none is honest. */
  explanation: string;
  patterns: RecognizedPattern[];
  /** Why confidence is below high; empty when confidence is high. */
  confidenceReasons: string[];
  /** Up to three named readings of the same function. */
  approaches: AlgorithmApproach[];
  /** Empty unless more than one approach is present. */
  selectionHint: string;
  tier: AnalysisTier;
}

export type ApproachRole =
  | 'dominant'
  | 'nested'
  | 'sequential'
  | 'alternative';

export interface AlgorithmApproach {
  id: string;
  name: string;
  summary: string;
  role: ApproachRole;
  timeHint?: string;
}

export interface RecognizedPattern {
  id: string;
  label: string;
  reason: string;
  effect?: 'annotate' | 'unknown' | 'range';
  range?: LineRange;
}

export interface AnalyzeRequest {
  uri: string;
  /** Full document text (versioned full sync). */
  text: string;
  version: number;
  tier: AnalysisTier;
  /** When set, analyze this span as a synthetic method body. */
  selection?: LineRange;
}

export interface AnalyzeResponse {
  uri: string;
  version: number;
  functions: FunctionComplexity[];
  warnings: AnalysisWarning[];
}

export interface SetSolutionContextRequest {
  /** Absolute path to the .sln/.csproj used for project-backed analysis. */
  solutionPath: string;
}

export interface InitializeResult {
  serverName: string;
  analyzerVersion: string;
}

export const ProtocolMethods = {
  initialize: 'initialize',
  analyze: 'ohno/analyze',
  analyzeDeep: 'ohno/analyzeDeep',
  setSolutionContext: 'ohno/setSolutionContext',
  shutdown: 'shutdown',
} as const;
