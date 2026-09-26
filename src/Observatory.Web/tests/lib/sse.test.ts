// @vitest-environment node
import { describe, expect, it, vi } from 'vitest';
import { readSseFrames } from './sse';
import type { SseFrame } from './sse';

function stream(text: string, chunkSize = 1): ReadableStream<Uint8Array> {
  const bytes = new TextEncoder().encode(text);
  return new ReadableStream({
    start(controller) {
      for (let index = 0; index < bytes.length; index += chunkSize) {
        controller.enqueue(bytes.slice(index, index + chunkSize));
      }
      controller.close();
    },
  });
}

async function frames(text: string): Promise<SseFrame[]> {
  const result: SseFrame[] = [];
  for await (const frame of readSseFrames(stream(text))) result.push(frame);
  return result;
}

describe('decodifica replay SSE da POST', () => {
  it('gestisce chunk su singoli byte UTF-8, CRLF, commenti e ID numerici', async () => {
    const result = await frames('\uFEFF: registrazione\r\nid: 12\r\ndata: {"message":"È difettosa"}\r\n\r\nid: 13\r\nevent: message\r\ndata: {"message":"Conferma"}\r\n\r\n');
    expect(result).toEqual([
      { event: 'message', id: '12', data: '{"message":"È difettosa"}' },
      { event: 'message', id: '13', data: '{"message":"Conferma"}' },
    ]);
  });

  it('supporta dati multilinea, newline CR/LF e conserva l’ultimo event id', async () => {
    expect(await frames('id: 1\ndata: {"value":\ndata: 2}\n\ndata: terzo\r\r: fine')).toEqual([
      { event: 'message', id: '1', data: '{"value":\n2}' },
      { event: 'message', id: '1', data: 'terzo' },
    ]);
  });

  it('non spaccia un ultimo evento troncato per una registrazione completa', async () => {
    await expect(frames('data: {"id":1}\n')).rejects.toThrow('Replay SSE troncato');
    await expect(frames('data: {"id":1}')).rejects.toThrow('Replay SSE troncato');
  });

  it('chiude il reader se il consumatore interrompe la decodifica', async () => {
    const cancel = vi.fn();
    const source = new ReadableStream<Uint8Array>({
      start(controller) { controller.enqueue(new TextEncoder().encode('data: first\n\ndata: second\n\n')); },
      cancel,
    });
    for await (const frame of readSseFrames(source)) {
      expect(frame.data).toBe('first');
      break;
    }
    expect(cancel).toHaveBeenCalledTimes(1);
    expect(source.locked).toBe(false);
  });
});
