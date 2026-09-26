// @vitest-environment node
import { describe, expect, it, vi } from 'vitest';
import { newId } from '../../src/lib/ids';

const uuidV4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;

describe('identificatori sicuri anche su HTTP LAN', () => {
  it('usa UUID nativi quando disponibili', () => {
    expect(newId()).toMatch(uuidV4);
    expect(newId()).not.toBe(newId());
  });

  it('genera UUID v4 con getRandomValues se randomUUID non è esposto', () => {
    const source = globalThis.crypto;
    const getRandomValues = vi.fn(source.getRandomValues.bind(source));
    vi.stubGlobal('crypto', { getRandomValues });
    const first = newId();
    const second = newId();
    expect(first).toMatch(uuidV4);
    expect(second).toMatch(uuidV4);
    expect(first).not.toBe(second);
    expect(getRandomValues).toHaveBeenCalledTimes(2);
  });

  it('fallisce esplicitamente se manca la crittografia, senza Math.random', () => {
    vi.stubGlobal('crypto', undefined);
    expect(() => newId()).toThrow('generatore casuale sicuro');
  });
});
