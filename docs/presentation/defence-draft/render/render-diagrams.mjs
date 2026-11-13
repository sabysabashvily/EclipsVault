import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { PeleError, render } from 'pele';

const here = dirname(fileURLToPath(import.meta.url));
const draft = join(here, '..');
const markdownPath = join(draft, 'diagrams.md');
const svgDir = join(draft, 'svg');
const htmlPath = join(draft, 'diagrams.html');

const files = {
  1: '01-context.svg',
  2: '02-container.svg',
  3: '03-web-component.svg',
  4: '04-reveal.svg',
  5: '05-deployment.svg',
};

const georgianTitles = {
  1: 'კონტექსტი',
  2: 'კონტეინერი',
  3: 'Web-ის კომპონენტი',
  4: 'გახსნის კოდი',
  5: 'განთავსება',
};

const fontFamily = 'DejaVu Sans, Noto Sans Georgian, sans-serif';

function extract(markdown) {
  const lines = markdown.split('\n');
  const diagrams = [];
  let current = null;
  for (let i = 0; i < lines.length; i++) {
    const heading = lines[i].match(/^## (\d+)\. (.+)$/);
    if (heading) {
      current = { number: Number(heading[1]), title: heading[2], source: null, caption: null };
      diagrams.push(current);
      continue;
    }
    if (current && lines[i] === '```mermaid') {
      const body = [];
      i++;
      while (i < lines.length && lines[i] !== '```') {
        body.push(lines[i]);
        i++;
      }
      current.source = body.join('\n');
      continue;
    }
    if (current && lines[i].startsWith('Caption / წარწერა:')) {
      const parts = [lines[i].slice('Caption / წარწერა:'.length).trim()];
      i++;
      while (
        i < lines.length &&
        lines[i].trim() !== '' &&
        !lines[i].startsWith('|') &&
        !lines[i].startsWith('#')
      ) {
        parts.push(lines[i].trim());
        i++;
      }
      current.caption = parts.join(' ');
      i--;
    }
  }
  return diagrams;
}

function splitCaption(caption) {
  const marker = ' / ';
  const at = caption.indexOf(marker);
  if (at === -1) return { en: caption, ka: '' };
  return { en: caption.slice(0, at).trim(), ka: caption.slice(at + marker.length).trim() };
}

function escapeText(value) {
  return value
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;');
}

const markdown = readFileSync(markdownPath, 'utf8');
const diagrams = extract(markdown);
const failures = [];

for (const diagram of diagrams) {
  if (!files[diagram.number]) failures.push(`No file name for section ${diagram.number}.`);
  if (!diagram.source) failures.push(`Section ${diagram.number} has no mermaid block.`);
  if (!diagram.caption) failures.push(`Section ${diagram.number} has no caption.`);
}

if (failures.length > 0) {
  for (const failure of failures) console.error(failure);
  process.exit(1);
}

mkdirSync(svgDir, { recursive: true });
const rendered = [];

for (const diagram of diagrams) {
  const file = files[diagram.number];
  try {
    const result = render(diagram.source, {
      fontFamily,
      idPrefix: `d${diagram.number}-`,
    });
    writeFileSync(join(svgDir, file), `${result.svg}\n`);
    rendered.push({ ...diagram, file, ...splitCaption(diagram.caption), svg: result.svg });
    console.log(`${file} ${result.type} ${result.width}x${result.height}`);
  } catch (error) {
    const message = error instanceof PeleError ? error.message : String(error);
    failures.push(`${file}: ${message}`);
    console.error(`${file}: ${message}`);
  }
}

if (rendered.length !== diagrams.length) {
  console.error('Pele did not render every diagram.');
  process.exit(1);
}

const figures = rendered
  .map(
    (diagram) => `  <article class="sheet" id="diagram-${diagram.number}">
    <h2>${escapeText(`${diagram.number}. ${diagram.title}`)} / ${escapeText(georgianTitles[diagram.number])}</h2>
    <div class="picture">${diagram.svg}</div>
    <p class="file"><a href="svg/${diagram.file}">svg/${diagram.file}</a></p>
    <p class="caption" lang="en">${escapeText(diagram.en)}</p>
    <p class="caption" lang="ka">${escapeText(diagram.ka)}</p>
  </article>`,
  )
  .join('\n');

const html = `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>EclipsVault diagrams / დიაგრამები</title>
  <style>
    :root {
      --pele-bg: #ffffff;
      --pele-fg: #1f1f1f;
      --pele-fg-on-light: #1f1f1f;
      --pele-fg-on-dark: #ffffff;
      --pele-muted: #6e6e6e;
      --pele-line: #1f1f1f;
      --pele-surface: #f3f3f3;
      --pele-surface-alt: #e6e6e6;
      --pele-border: #8a8a8a;
      --pele-accent: #5b7bd5;
      --pele-critical: #e45756;
      --pele-error: #e45756;
      --pele-font: "DejaVu Sans", "Noto Sans Georgian", sans-serif;
      --pele-font-mono: "DejaVu Sans Mono", ui-monospace, monospace;
      --pele-radius: 4px;
    }
    @media (prefers-color-scheme: dark) {
      :root {
        --pele-bg: #121417;
        --pele-fg: #f2f2ef;
        --pele-fg-on-light: #1f1f1f;
        --pele-fg-on-dark: #ffffff;
        --pele-muted: #b8b6b0;
        --pele-line: #f2f2ef;
        --pele-surface: #1c1b1a;
        --pele-surface-alt: #282726;
        --pele-border: #403e3c;
        --pele-accent: #8eabe8;
      }
    }
    @media print {
      :root {
        --pele-bg: #ffffff;
        --pele-fg: #1f1f1f;
        --pele-muted: #333333;
        --pele-line: #1f1f1f;
        --pele-surface: #f3f3f3;
        --pele-surface-alt: #e6e6e6;
        --pele-border: #8a8a8a;
        --pele-accent: #5b7bd5;
      }
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      background: var(--pele-bg);
      color: var(--pele-fg);
      font-family: var(--pele-font);
      line-height: 1.45;
    }
    main { max-width: 70rem; margin: 0 auto; padding: 1.5rem; }
    h1, h2 { font-weight: 650; line-height: 1.2; }
    h1 { font-size: 1.6rem; margin: 0 0 0.4rem; }
    .intro { color: var(--pele-muted); margin: 0 0 1.5rem; }
    .sheet {
      break-inside: avoid;
      page-break-inside: avoid;
      margin: 0 0 2rem;
      padding-bottom: 1rem;
      border-bottom: 1px solid var(--pele-border);
    }
    h2 { font-size: 1.2rem; margin: 0 0 0.8rem; }
    .picture {
      background: var(--pele-bg);
      border: 1px solid var(--pele-border);
      border-radius: var(--pele-radius);
      padding: 0.75rem;
      overflow: auto;
    }
    .picture svg { display: block; margin: 0 auto; }
    .file { margin: 0.4rem 0 0; font-family: var(--pele-font-mono); font-size: 0.85rem; }
    .file a { color: var(--pele-accent); }
    .caption { margin: 0.35rem 0 0; }
    @page { margin: 14mm; }
  </style>
</head>
<body>
<main>
  <h1>EclipsVault diagrams / დიაგრამები</h1>
  <p class="intro" lang="en">Printable copy of the five C4 pictures in diagrams.md. Colours and type follow Pele's CSS variables. This page does not show a running vault.</p>
  <p class="intro" lang="ka">diagrams.md-ის ხუთი C4 სურათის საბეჭდი ასლი. ფერები და შრიფტი Pele-ის CSS ცვლადებს მიჰყვება. ეს გვერდი გაშვებულ საცავს არ აჩვენებს.</p>
${figures}
</main>
</body>
</html>
`;

writeFileSync(htmlPath, html);
console.log(`wrote ${rendered.length} diagrams`);
