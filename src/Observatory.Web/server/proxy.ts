import type { ProxyOptions } from 'vite';

export const backendVariables = {
  inline: 'INLINE_API_URL',
  skills: 'SKILLS_API_URL',
  a2a: 'A2A_API_URL',
} as const;

export function createApiProxy(environment: Record<string, string | undefined>): Record<string, ProxyOptions> {
  const proxy: Record<string, ProxyOptions> = {};
  const missing = Object.values(backendVariables).filter((name) => !environment[name]?.trim());
  if (missing.length > 0) {
    throw new Error(
      `Proxy API non configurato: manca ${missing.join(', ')}. ` +
      'Avvia tramite Observatory.AppHost oppure imposta gli URL dei tre backend. Nessun backend fittizio è previsto.',
    );
  }

  for (const [technology, variable] of Object.entries(backendVariables)) {
    const raw = environment[variable]?.trim();
    if (!raw) throw new Error(`Variabile obbligatoria: ${variable}`);
    let target: URL;
    try {
      target = new URL(raw);
    } catch {
      throw new Error(`${variable} deve contenere un URL HTTP(S) assoluto.`);
    }
    if (
      !['http:', 'https:'].includes(target.protocol) ||
      target.username || target.password || target.search || target.hash ||
      (target.pathname !== '/' && target.pathname !== '')
    ) {
      throw new Error(`${variable}: usa soltanto origine HTTP(S), senza credenziali, percorso o query.`);
    }
    proxy[`^/api/${technology}(?:/|$)`] = {
      target: target.origin,
      changeOrigin: true,
      rewrite: (path) => path.replace(new RegExp(`^/api/${technology}(?=/|$)`), '/api'),
    };
  }
  return proxy;
}
