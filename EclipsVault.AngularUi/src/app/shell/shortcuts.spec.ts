import { chordLabel, chordName, chords, isApplePlatform, pressed, typing } from './shortcuts';

const key = (init: KeyboardEventInit) => new KeyboardEvent('keydown', init);

describe('isApplePlatform', () => {
  it('recognizes Apple platforms only', () => {
    expect(isApplePlatform('MacIntel')).toBe(true);
    expect(isApplePlatform('iPhone')).toBe(true);
    expect(isApplePlatform('Win32')).toBe(false);
    expect(isApplePlatform('Linux x86_64')).toBe(false);
  });
});

describe('pressed', () => {
  it('takes ⌘ on Apple platforms and Ctrl elsewhere, never the other one', () => {
    expect(pressed(key({ key: 'b', metaKey: true }), chords.compact, true)).toBe(true);
    expect(pressed(key({ key: 'b', ctrlKey: true }), chords.compact, true)).toBe(false);
    expect(pressed(key({ key: 'b', ctrlKey: true }), chords.compact, false)).toBe(true);
    expect(pressed(key({ key: 'b', metaKey: true }), chords.compact, false)).toBe(false);
  });

  it('needs exactly the chord’s Shift and no Alt', () => {
    expect(pressed(key({ key: 'P', ctrlKey: true, shiftKey: true }), chords.searchAlt, false)).toBe(
      true,
    );
    expect(pressed(key({ key: 'p', ctrlKey: true }), chords.searchAlt, false)).toBe(false);
    expect(pressed(key({ key: 'K', ctrlKey: true, shiftKey: true }), chords.search, false)).toBe(
      false,
    );
    expect(pressed(key({ key: '.', ctrlKey: true, altKey: true }), chords.peek, false)).toBe(false);
    expect(pressed(key({ key: '.', ctrlKey: true }), chords.peek, false)).toBe(true);
  });
});

describe('chordLabel and chordName', () => {
  it('spell the chord for this keyboard', () => {
    expect(chordLabel(chords.search, true)).toBe('⌘K');
    expect(chordLabel(chords.search, false)).toBe('Ctrl K');
    expect(chordLabel(chords.searchAlt, true)).toBe('⌘⇧P');
    expect(chordLabel(chords.searchAlt, false)).toBe('Ctrl Shift P');
    expect(chordLabel(chords.peek, true)).toBe('⌘.');
    expect(chordName(chords.compact, true)).toBe('Meta+B');
    expect(chordName(chords.searchAlt, false)).toBe('Control+Shift+P');
  });
});

describe('typing', () => {
  it('is true in inputs, textareas and editable content, false elsewhere', () => {
    const editable = document.createElement('div');
    editable.setAttribute('contenteditable', 'true');
    const inside = document.createElement('span');
    editable.append(inside);
    const readOnly = document.createElement('div');
    readOnly.setAttribute('contenteditable', 'false');

    expect(typing(document.createElement('input'))).toBe(true);
    expect(typing(document.createElement('textarea'))).toBe(true);
    expect(typing(inside)).toBe(true);
    expect(typing(readOnly)).toBe(false);
    expect(typing(document.createElement('button'))).toBe(false);
    expect(typing(null)).toBe(false);
  });
});
