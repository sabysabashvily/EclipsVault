/**
 * The file name the server chose: `filename*` when present, since it carries non-ASCII names
 * exactly (RFC 6266), otherwise `filename`. Empty when there is neither, so the browser names it.
 */
export function attachmentName(disposition: string | null): string {
  const extended = /filename\*=UTF-8''([^;]+)/i.exec(disposition ?? '');
  if (extended) return decodeURIComponent(extended[1]);
  return /filename="?([^";]+)"?/i.exec(disposition ?? '')?.[1] ?? '';
}

export function saveAttachment(document: Document, file: Blob, name: string): void {
  const url = URL.createObjectURL(file);
  const link = document.createElement('a');
  link.href = url;
  link.download = name;
  link.click();
  // The browser has taken the file; keeping the URL would keep a copy of the data in memory.
  URL.revokeObjectURL(url);
}
