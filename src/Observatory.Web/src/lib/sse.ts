export interface SseFrame {
  event: string;
  id: string | null;
  data: string;
}

export async function* readSseFrames(body: ReadableStream<Uint8Array>): AsyncGenerator<SseFrame> {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  let data: string[] = [];
  let event = '';
  let id: string | null = null;
  let firstLine = true;
  let bytes = 0;
  let completed = false;
  try {
    while (true) {
      const chunk = await reader.read();
      completed = chunk.done;
      if (chunk.value) {
        bytes += chunk.value.byteLength;
        if (bytes > 32 * 1024 * 1024) throw new Error('Replay oltre il limite di visualizzazione di 32 MiB. Usa l’export del run.');
      }
      buffer += decoder.decode(chunk.value, { stream: !chunk.done });
      while (true) {
        const newline = buffer.search(/[\r\n]/);
        if (newline < 0) break;
        if (buffer[newline] === '\r' && newline === buffer.length - 1 && !chunk.done) break;
        let line = buffer.slice(0, newline);
        const separatorLength = buffer[newline] === '\r' && buffer[newline + 1] === '\n' ? 2 : 1;
        buffer = buffer.slice(newline + separatorLength);
        if (firstLine) {
          line = line.replace(/^\uFEFF/, '');
          firstLine = false;
        }
        if (line === '') {
          if (data.length > 0) yield { event: event || 'message', id, data: data.join('\n') };
          data = [];
          event = '';
          continue;
        }
        if (line.startsWith(':')) continue;
        const colon = line.indexOf(':');
        const field = colon < 0 ? line : line.slice(0, colon);
        let value = colon < 0 ? '' : line.slice(colon + 1);
        if (value.startsWith(' ')) value = value.slice(1);
        if (field === 'data') data.push(value);
        else if (field === 'event') event = value;
        else if (field === 'id' && !value.includes('\0')) id = value;
      }
      if (chunk.done) {
        if (data.length > 0 || buffer.startsWith('data:') || buffer === 'data') {
          throw new Error('Replay SSE troncato: l’ultimo evento non è terminato correttamente.');
        }
        return;
      }
    }
  } finally {
    try {
      if (!completed) await reader.cancel();
    } finally {
      reader.releaseLock();
    }
  }
}
