export function newId(): string {
  const cryptography = globalThis.crypto;
  if (typeof cryptography?.randomUUID === 'function') return cryptography.randomUUID();
  if (typeof cryptography?.getRandomValues !== 'function') {
    throw new Error('Il browser non offre un generatore casuale sicuro. Usa un browser moderno su localhost o HTTPS.');
  }
  const bytes = new Uint8Array(16);
  cryptography.getRandomValues(bytes);
  const view = new DataView(bytes.buffer);
  view.setUint8(6, (view.getUint8(6) & 0x0f) | 0x40);
  view.setUint8(8, (view.getUint8(8) & 0x3f) | 0x80);
  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
