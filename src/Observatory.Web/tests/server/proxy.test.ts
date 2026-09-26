// @vitest-environment node
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { createApiProxy } from './proxy';

describe('proxy server-only', () => {
  const environment = {
    INLINE_API_URL: 'http://inline-api:8080/',
    SKILLS_API_URL: 'http://skills-api:8080',
    A2A_API_URL: 'https://a2a-api.example.test',
  };
  it('richiede i tre target, senza fallback su localhost', () => {
    expect(() => createApiProxy({})).toThrow('INLINE_API_URL, SKILLS_API_URL, A2A_API_URL');
    expect(() => createApiProxy({ INLINE_API_URL: environment.INLINE_API_URL })).toThrow('SKILLS_API_URL');
  });

  it('mantiene tutte le route pubbliche e riscrive soltanto il prefisso API', () => {
    const proxies = createApiProxy(environment);
    for (const technology of ['inline', 'skills', 'a2a']) {
      const proxy = proxies[`^/api/${technology}(?:/|$)`];
      expect(proxy?.rewrite?.(`/api/${technology}/runs/1/events?x=1`)).toBe('/api/runs/1/events?x=1');
      expect(proxy?.rewrite?.(`/api/${technology}-other/products`)).toBe(`/api/${technology}-other/products`);
    }
    expect(proxies['^/api/inline(?:/|$)']?.target).toBe('http://inline-api:8080');
  });

  it('rifiuta credenziali, query, percorsi e protocolli non HTTP', () => {
    for (const value of ['ftp://api', 'http://user:secret@api', 'http://api/path', 'http://api?token=secret', 'not-a-url']) {
      expect(() => createApiProxy({ ...environment, INLINE_API_URL: value })).toThrow();
    }
  });

  it('il container usa root context, template relativo, SSE senza buffering e health', () => {
    const dockerfile = readFileSync(resolve(process.cwd(), 'Dockerfile'), 'utf8');
    const nginx = readFileSync(resolve(process.cwd(), 'nginx', 'default.conf.template'), 'utf8');
    expect(dockerfile).toContain('FROM node:24-alpine');
    expect(dockerfile).toContain('FROM nginx:alpine');
    expect(dockerfile).toContain('COPY src/Observatory.Web/package.json src/Observatory.Web/package-lock.json');
    expect(dockerfile).toContain('NGINX_ENVSUBST_FILTER');
    expect(nginx).toContain('proxy_buffering off;');
    expect(nginx).toContain('proxy_next_upstream off;');
    expect(nginx).toContain('location = /health');
    expect(nginx).toContain('${INLINE_API_URL}/api/');
    expect(nginx).toContain('${SKILLS_API_URL}/api/');
    expect(nginx).toContain('${A2A_API_URL}/api/');
    expect(nginx).toContain('Host $proxy_host');
    expect(nginx).toContain('try_files $uri $uri/ /index.html');
  });
});
