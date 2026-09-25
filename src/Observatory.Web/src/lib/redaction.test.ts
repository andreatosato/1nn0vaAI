// @vitest-environment node
import { describe, expect, it } from 'vitest';
import { redact, redactText, safeJson } from './redaction';
import { money, publicThumbnail, tokens } from './format';

describe('redazione difensiva locale', () => {
  it('nasconde chiavi sensibili anche in corpi wire JSON annidati senza distruggere i conteggi token', () => {
    const result = safeJson({
      Authorization: 'Bearer private-value', 'x-api-key': 'key-value', client_secret: 'secret-value',
      'Set-Cookie': 'session=private-cookie', inputTokens: 123, maxOutputTokens: 1500,
      body: '{"headers":{"api_key":"nested-private"},"usage":{"output_tokens":12}}',
      endpoint: 'https://api.example.test/path?api-key=private-query&version=1',
      AzureOpenAI__ApiKey: 'provider-private',
      headers: [{ name: 'api-key', value: 'header-array-private' }],
    });
    for (const secret of ['private-value', 'key-value', 'secret-value', 'private-cookie', 'nested-private', 'private-query', 'provider-private', 'header-array-private']) {
      expect(result).not.toContain(secret);
    }
    expect(result).toContain('[REDACTED]');
    expect(result).toContain('123');
    expect(result).toContain('1500');
    expect(result).toContain('version=1');
  });

  it('oscura header testuali, credenziali URL e JSON troncato', () => {
    expect(redactText('Authorization: Bearer abc.def.ghi')).not.toContain('abc.def.ghi');
    expect(redactText('{"api_key":"truncated-secret"')).not.toContain('truncated-secret');
    expect(redactText('https://username:password@example.test')).not.toContain('username:password');
  });

  it('gestisce strutture cicliche senza eseguire il contenuto', () => {
    const value: Record<string, unknown> = { text: '<script>alert(1)</script>' };
    value.self = value;
    expect(redact(value)).toEqual({ text: '<script>alert(1)</script>', self: '[Riferimento circolare]' });
  });
});

describe('presentazione senza metriche inventate', () => {
  it('distingue valore mancante da zero misurato', () => {
    expect(tokens(null)).toBe('Non disponibile');
    expect(tokens(0)).toBe('0');
    expect(money(null)).toBe('Non disponibile');
  });

  it('accetta solo thumbnail pubbliche DummyJSON HTTPS', () => {
    expect(publicThumbnail('https://cdn.dummyjson.com/product-images/test.webp')).toBe('https://cdn.dummyjson.com/product-images/test.webp');
    for (const url of ['http://cdn.dummyjson.com/x', 'https://localhost/x', 'https://127.0.0.1/x', 'data:image/svg+xml,x', 'javascript:alert(1)', 'https://user:password@dummyjson.com/x', 'https://cdn.dummyjson.com/x?token=private']) {
      expect(publicThumbnail(url)).toBeNull();
    }
  });
});
