import { DOCUMENT, inject, InjectionToken } from '@angular/core';

/** ⌘ is an Apple key, so everyone else holds Ctrl. */
export function isApplePlatform(platform: string): boolean {
  return /Mac|iPhone|iPad|iPod/.test(platform);
}

/** Whether this browser runs on an Apple platform; replaceable in tests. */
export const APPLE_PLATFORM = new InjectionToken<boolean>('APPLE_PLATFORM', {
  factory: () => {
    const navigator = inject(DOCUMENT).defaultView?.navigator;
    return isApplePlatform(navigator?.platform || navigator?.userAgent || '');
  },
});

export interface Chord {
  readonly key: string;
  readonly shift?: boolean;
}

/** The only browser shortcuts the vault takes. */
export const chords = {
  search: { key: 'k' },
  searchAlt: { key: 'p', shift: true },
  compact: { key: 'b' },
  peek: { key: '.' },
} as const satisfies Record<string, Chord>;

/** ⌘ alone on Apple platforms, Ctrl alone elsewhere, with exactly the chord's Shift. */
export function pressed(event: KeyboardEvent, chord: Chord, apple: boolean): boolean {
  const modifier = apple ? event.metaKey && !event.ctrlKey : event.ctrlKey && !event.metaKey;
  return (
    modifier &&
    !event.altKey &&
    event.shiftKey === !!chord.shift &&
    event.key.toLowerCase() === chord.key
  );
}

/** The chord as this keyboard spells it: "⌘⇧P" on a Mac, "Ctrl Shift P" elsewhere. */
export function chordLabel(chord: Chord, apple: boolean): string {
  const key = chord.key.toUpperCase();
  return apple ? `⌘${chord.shift ? '⇧' : ''}${key}` : `Ctrl ${chord.shift ? 'Shift ' : ''}${key}`;
}

/** The name assistive technology expects in aria-keyshortcuts, such as "Meta+B". */
export function chordName(chord: Chord, apple: boolean): string {
  return [apple ? 'Meta' : 'Control', chord.shift ? 'Shift' : '', chord.key.toUpperCase()]
    .filter(Boolean)
    .join('+');
}

/** A field that takes text, where a letter or punctuation chord may be an editing command. */
export function typing(target: EventTarget | null): boolean {
  if (!(target instanceof Element)) return false;
  if (target.closest('input, textarea')) return true;
  const editor = target.closest(
    '[contenteditable=""], [contenteditable="true" i], [contenteditable="false" i], [contenteditable="plaintext-only" i]',
  );
  return editor !== null && editor.getAttribute('contenteditable')?.toLowerCase() !== 'false';
}
