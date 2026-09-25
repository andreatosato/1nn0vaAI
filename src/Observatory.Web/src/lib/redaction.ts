export type JsonValue = null | string | number | boolean | JsonValue[] | { [key: string]: JsonValue };
const hidden = '[REDACTED]';

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function secretKey(key: string): boolean {
  const normalized = key.toLowerCase().replace(/[^a-z0-9]/g, '');
  return /password|secret|credential|privatekey|connectionstring/.test(normalized) ||
    /apikey|subscriptionkey|accesskey|accountkey/.test(normalized) ||
    /^(?:authorization|proxyauthorization|cookie|setcookie|token|apikey|xapikey|subscriptionkey|ocpapimsubscriptionkey|accesskey|accountkey|sig|signature)$/.test(normalized) ||
    /(?:accesstoken|refreshtoken|idtoken|bearertoken|authtoken|apitoken)$/.test(normalized);
}

export function redactText(value: string): string {
  return value
    .replace(/\b(Bearer|Basic)\s+[a-z0-9+/_=.~:-]+/gi, `$1 ${hidden}`)
    .replace(/\b(sk-[a-z0-9_-]{12,}|gh[pousr]_[a-z0-9]{20,}|github_pat_[a-z0-9_]{20,})\b/gi, hidden)
    .replace(/(https?:\/\/)[^\s/@]+:[^\s/@]+@/gi, `$1${hidden}@`)
    .replace(/([?&](?:api[-_]?key|access[-_]?token|token|secret|password|sig|signature|code)=)[^&\s"'<>]+/gi, `$1${hidden}`)
    .replace(/((?:"|')?(?:api[-_]?key|client[-_]?secret|password|authorization|access[-_]?token)(?:"|')?\s*[:=]\s*)("[^"]*"|'[^']*'|[^\s,;}&]+)/gi, `$1${hidden}`);
}

export function redact(value: unknown, seen = new WeakSet<object>(), depth = 0): JsonValue {
  if (depth > 40) return '[Profondità massima di visualizzazione]';
  if (value === null || value === undefined) return null;
  if (typeof value === 'boolean' || typeof value === 'number') return value;
  if (typeof value === 'string') {
    const trimmed = value.trim();
    if ((trimmed.startsWith('{') && trimmed.endsWith('}')) || (trimmed.startsWith('[') && trimmed.endsWith(']'))) {
      try {
        const nested: unknown = JSON.parse(trimmed);
        return JSON.stringify(redact(nested, seen, depth + 1), null, 2);
      } catch {
        // A wire body can be truncated JSON; redact its text even when parsing fails.
      }
    }
    return redactText(value);
  }
  if (typeof value !== 'object') return String(value);
  if (seen.has(value)) return '[Riferimento circolare]';
  seen.add(value);
  if (Array.isArray(value)) {
    return value.map((item: unknown) => redact(item, seen, depth + 1));
  }
  if (isRecord(value)) {
    const headerName = typeof value.name === 'string' ? value.name : typeof value.key === 'string' ? value.key : null;
    const sensitiveHeader = headerName !== null && secretKey(headerName);
    return Object.fromEntries(Object.entries(value).map(([key, item]) => [
      key, secretKey(key) || (sensitiveHeader && /^(value|values)$/i.test(key))
        ? hidden : redact(item, seen, depth + 1),
    ]));
  }
  return String(value);
}

export function safeJson(value: unknown): string {
  return JSON.stringify(redact(value), null, 2);
}

export function errorMessage(error: unknown): string {
  return redactText(error instanceof Error ? error.message : 'Errore inatteso. Riprova esplicitamente.');
}

export function isAbort(error: unknown): boolean {
  return error instanceof Error && error.name === 'AbortError';
}
