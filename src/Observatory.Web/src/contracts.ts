import { z } from 'zod';

export const technologies = ['inline', 'skills', 'a2a'] as const;
export const agents = ['Router', 'Catalog', 'Orders', 'Returns'] as const;
export const services = ['catalog', 'orders', 'returns'] as const;
export type Technology = (typeof technologies)[number];
export type Agent = (typeof agents)[number];
export type Service = (typeof services)[number];
export const agentIds = {
  Router: 'router', Catalog: 'catalog', Orders: 'orders', Returns: 'returns',
} as const satisfies Record<Agent, string>;
export const promptBlockIds = ['checklist', 'outputContract', 'examples', 'redundancy', 'conflictingStyle'] as const;
export type PromptBlockId = (typeof promptBlockIds)[number];
export const defaultPromptBlocks = {
  checklist: false, outputContract: false, examples: false, redundancy: false, conflictingStyle: false,
} satisfies Record<PromptBlockId, boolean>;
export const promptBlocksSchema = z.object({
  checklist: z.boolean(),
  outputContract: z.boolean(),
  examples: z.boolean(),
  redundancy: z.boolean(),
  conflictingStyle: z.boolean(),
}).strict();
export type PromptBlocks = z.infer<typeof promptBlocksSchema>;

const nullableNumber = z.number().finite().nullable();
const optionalString = z.string().nullish();
const timestamp = z.string().refine((value) => Number.isFinite(Date.parse(value)), 'Date invalide');

export const productSchema = z.object({
  id: z.number().int(),
  title: z.string(),
  description: z.string(),
  category: z.string(),
  price: z.number().finite(),
  currency: z.string(),
  stock: z.number().int(),
  brand: optionalString,
  sku: z.string(),
  thumbnail: optionalString,
  images: z.array(z.string()),
  tags: z.array(z.string()),
});
export type Product = z.infer<typeof productSchema>;

export const shopOrderSchema = z.object({
  id: z.string().min(1),
  customerId: z.string().min(1),
  productId: z.number().int(),
  productTitle: z.string(),
  listPrice: z.number().finite().nonnegative(),
  amountPaid: z.number().finite().nonnegative(),
  currency: z.string(),
  outlet: z.boolean(),
  deliveredAt: z.iso.date(),
  status: z.string(),
  trackingCode: z.string(),
});
export const shopPolicySchema = z.object({
  id: z.string().min(1),
  title: z.string(),
  text: z.string(),
  priority: z.number().int(),
  version: z.string(),
});
export const demoDataSchema = z.object({
  asOf: timestamp,
  customerId: z.string().min(1),
  notice: z.string(),
  orders: z.array(shopOrderSchema),
  policies: z.array(shopPolicySchema),
});
export type DemoData = z.infer<typeof demoDataSchema>;

export const modelDefinitionSchema = z.object({
  id: z.string().min(1),
  name: z.string(),
  family: z.string(),
  modelId: z.string(),
  modelVersion: optionalString,
  deployment: optionalString,
  region: optionalString,
  deploymentType: optionalString,
  configured: z.boolean(),
  pricing: z.object({
    inputPerMillion: nullableNumber,
    cachedInputPerMillion: nullableNumber,
    outputPerMillion: nullableNumber,
    cacheWriteSurchargePerMillion: nullableNumber,
    cacheWritePerMillion: nullableNumber.optional(),
    longContextThresholdTokens: z.number().int().positive().nullish(),
    longContextInputPerMillion: nullableNumber.optional(),
    longContextCachedInputPerMillion: nullableNumber.optional(),
    longContextCacheWritePerMillion: nullableNumber.optional(),
    longContextOutputPerMillion: nullableNumber.optional(),
    currency: z.string(),
    sourceUrl: optionalString,
    verifiedAt: optionalString,
    version: z.string(),
  }),
});
export type ModelDefinition = z.infer<typeof modelDefinitionSchema>;

export const demoConfigurationSchema = z.object({
  technology: z.enum(technologies),
  defaultMode: z.string(),
  allowLive: z.boolean(),
  models: z.array(modelDefinitionSchema),
  promptProfiles: z.array(z.string()),
  promptBlocks: z.array(z.object({
    id: z.enum(promptBlockIds),
    label: z.string().min(1),
    description: z.string(),
  })).refine((blocks) => new Set(blocks.map((block) => block.id)).size === blocks.length, 'Blocchi duplicati'),
  historyStrategies: z.array(z.string()),
  capabilities: z.object({
    agentNames: z.array(z.enum(['router', ...services])).min(1),
    serviceNames: z.array(z.enum(services)),
    businessApi: z.boolean(),
    executionTopology: z.enum(['router-http', 'router-skills-http', 'router-a2a']),
    modelCapabilities: z.array(z.object({
      modelProfileId: z.string(),
      liveReady: z.boolean(),
    })).optional(),
    maxApprovedBudgetUsd: z.number().positive().optional(),
    allowUnboundedExecution: z.boolean().optional(),
  }),
  asOf: timestamp,
  dataNotice: z.string(),
  catalogSourceUrl: optionalString,
  catalogRetrievedAt: timestamp.nullish(),
  catalogHash: optionalString,
});
export type DemoConfiguration = z.infer<typeof demoConfigurationSchema>;

export const promptProfileSchema = z.enum(['bad', 'good', 'gpt5', 'gpt6']);
export const historyStrategySchema = z.enum(['full', 'compact']);
export type PromptProfile = z.infer<typeof promptProfileSchema>;
export type HistoryStrategy = z.infer<typeof historyStrategySchema>;

export const runConfigurationSchema = z.object({
  mode: z.string().min(1),
  modelProfileId: z.string().min(1),
  agentModels: z.record(z.string(), z.string()),
  promptProfile: promptProfileSchema,
  promptBlocks: promptBlocksSchema.default(() => ({ ...defaultPromptBlocks })),
  historyStrategy: historyStrategySchema,
  toolTransport: z.literal('direct'),
  confirmAction: z.boolean(),
  maxOutputTokens: z.number().int().positive(),
  maxModelCalls: z.number().int().positive(),
  approvedBudgetUsd: z.number().finite().positive().nullish(),
  unboundedExecution: z.boolean().optional(),
});
export type RunConfiguration = z.infer<typeof runConfigurationSchema>;

export const promptPreviewSchema = z.object({
  technology: z.enum(technologies),
  agents: z.array(z.object({
    agent: z.enum(['router', ...services]),
    instructions: z.string(),
    characterCount: z.number().int().nonnegative(),
  })).min(1),
  notice: z.string(),
});
export type PromptPreview = z.infer<typeof promptPreviewSchema>;

export const scenarioSchema = z.object({
  id: z.string(),
  name: z.string(),
  group: z.string(),
  split: z.string(),
  turns: z.array(z.object({
    message: z.string(),
    expectedIntent: z.string(),
    expectedFacts: z.array(z.string()),
    confirmAction: z.boolean(),
  })),
});
export type ScenarioDefinition = z.infer<typeof scenarioSchema>;

export const chatMessageSchema = z.object({
  id: z.string(),
  role: z.string(),
  text: z.string(),
  at: timestamp,
  runId: optionalString,
  productIds: z.array(z.number().int()),
  sources: z.array(z.string()),
});
export const conversationSchema = z.object({
  id: z.string(),
  technology: z.enum(technologies),
  title: z.string(),
  createdAt: timestamp,
  messages: z.array(chatMessageSchema),
});
export type ConversationRecord = z.infer<typeof conversationSchema>;

export const runEventSchema = z.object({
  id: z.string().min(1),
  sequence: z.number().int().nonnegative(),
  runId: z.string().min(1),
  at: timestamp,
  kind: z.string().min(1),
  agent: z.string(),
  message: z.string(),
  data: z.unknown(),
});
export type RunEvent = z.infer<typeof runEventSchema>;

export const replayCaptureSchema = z.object({
  contentType: z.string(),
  originalRunId: z.string().min(1),
  replayOnly: z.literal(true),
  events: z.array(runEventSchema),
});
export type ReplayCapture = z.infer<typeof replayCaptureSchema>;

export const wireAttemptSchema = z.object({
  id: z.string(),
  endpoint: optionalString,
  requestBody: optionalString,
  responseBody: optionalString,
  statusCode: z.number().int().nullish(),
  durationMs: z.number().finite().nonnegative(),
  truncated: z.boolean(),
  error: optionalString,
});
export const modelCallSchema = z.object({
  id: z.string().min(1),
  runId: z.string(),
  agent: z.string(),
  modelProfileId: z.string(),
  mode: z.string(),
  modelId: optionalString,
  deployment: optionalString,
  providerResponseId: optionalString,
  traceId: optionalString,
  spanId: optionalString,
  startedAt: timestamp,
  durationMs: z.number().finite().nonnegative(),
  inputTokens: nullableNumber,
  cachedInputTokens: nullableNumber,
  cacheWriteTokens: nullableNumber,
  outputTokens: nullableNumber,
  reasoningTokens: nullableNumber,
  usageSource: z.string(),
  estimatedCostUsd: nullableNumber,
  costStatus: z.string(),
  captureKind: z.string(),
  status: z.string(),
  request: z.unknown(),
  response: z.unknown(),
  rawUsage: z.unknown(),
  attempts: z.array(wireAttemptSchema),
  error: optionalString,
});
export type ModelCallRecord = z.infer<typeof modelCallSchema>;

export const runRecordSchema = z.object({
  id: z.string().min(1),
  conversationId: z.string(),
  technology: z.enum(technologies),
  status: z.string(),
  message: z.string(),
  configuration: runConfigurationSchema,
  startedAt: timestamp,
  completedAt: timestamp.nullable(),
  durationMs: nullableNumber,
  timeToFirstAnswerMs: nullableNumber,
  result: z.object({
    answer: z.string(),
    productIds: z.array(z.number().int()),
    sources: z.array(z.string()),
    decision: optionalString,
  }).nullable(),
  error: optionalString,
  events: z.array(runEventSchema),
  calls: z.array(modelCallSchema),
  inputTokens: nullableNumber,
  outputTokens: nullableNumber,
  estimatedCostUsd: nullableNumber,
  costStatus: z.string(),
  scenarioId: optionalString,
  experimentId: optionalString,
});
export type RunRecord = z.infer<typeof runRecordSchema>;

export const turnAcceptedSchema = z.object({
  runId: z.string().min(1),
  conversationId: z.string().min(1),
  eventsUrl: z.string().min(1),
});
export type TurnAccepted = z.infer<typeof turnAcceptedSchema>;
export interface SubmitTurnRequest {
  message: string;
  idempotencyKey: string;
  configuration: RunConfiguration;
}
export interface ExperimentRequest {
  scenarioIds: string[];
  configurations: RunConfiguration[];
  repetitions: number;
  dryRun: boolean;
}
