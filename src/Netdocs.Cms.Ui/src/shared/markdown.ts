// HTML → markdown for blocks edited visually, plus the catalogue of insertable blocks.
import TurndownService from 'turndown';
// @ts-expect-error: the gfm plugin ships without type declarations.
import { gfm } from 'turndown-plugin-gfm';

let service: TurndownService | null = null;

function turndown(): TurndownService {
  if (service) return service;
  const t = new TurndownService({
    headingStyle: 'atx',
    hr: '---',
    bulletListMarker: '-',
    codeBlockStyle: 'fenced',
    emDelimiter: '*',
    strongDelimiter: '**',
    linkStyle: 'inlined',
  });
  t.use(gfm);

  // Links and images: write back the original markdown target, not the URL the build rewrote.
  t.addRule('mdLink', {
    filter: (node) => node.nodeName === 'A' && !!(node as HTMLElement).getAttribute('href'),
    replacement: (content, node) => {
      const el = node as HTMLElement;
      if (el.classList.contains('headerlink')) return '';
      const href = el.getAttribute('data-md-href') ?? el.getAttribute('href') ?? '';
      const title = el.getAttribute('title');
      if (!content.trim()) return '';
      return `[${content}](${escapeUrl(href)}${title ? ` "${title.replace(/"/g, '\\"')}"` : ''})`;
    },
  });
  t.addRule('mdImage', {
    filter: (node) => node.nodeName === 'IMG' && !(node as HTMLElement).classList.contains('twemoji') && !(node as HTMLElement).classList.contains('emojione'),
    replacement: (_content, node) => {
      const el = node as HTMLImageElement;
      const src = el.getAttribute('data-md-src') ?? el.getAttribute('src') ?? '';
      const alt = (el.getAttribute('alt') ?? '').replace(/[[\]]/g, '');
      const title = el.getAttribute('title');
      return `![${alt}](${escapeUrl(src)}${title ? ` "${title.replace(/"/g, '\\"')}"` : ''})`;
    },
  });
  // Emoji and icons come back as their shortcode.
  t.addRule('emoji', {
    filter: (node) => {
      const el = node as HTMLElement;
      return (el.classList?.contains('twemoji') || el.classList?.contains('emojione')) && !!(el.getAttribute('title') || el.getAttribute('alt'));
    },
    replacement: (_c, node) => {
      const el = node as HTMLElement;
      const code = el.getAttribute('title') || el.getAttribute('alt') || '';
      return code.startsWith(':') ? code : `:${code}:`;
    },
  });
  t.addRule('mark', { filter: ['mark'], replacement: (c) => `==${c}==` });
  t.addRule('del', { filter: ['del', 's'], replacement: (c) => `~~${c}~~` });
  t.addRule('sub', { filter: ['sub'], replacement: (c) => `~${c}~` });
  t.addRule('sup', {
    filter: (node) => node.nodeName === 'SUP' && !(node as HTMLElement).querySelector('a.footnote-ref'),
    replacement: (c) => `^${c}^`,
  });
  t.addRule('footnoteRef', {
    filter: (node) => node.nodeName === 'SUP' && !!(node as HTMLElement).querySelector('a.footnote-ref'),
    replacement: (_c, node) => {
      const href = (node as HTMLElement).querySelector('a')?.getAttribute('href') ?? '';
      return `[^${href.replace(/^#fn:?/, '')}]`;
    },
  });
  t.addRule('abbr', { filter: ['abbr'], replacement: (c) => c });
  t.addRule('kbd', {
    filter: (node) => node.nodeName === 'SPAN' && (node as HTMLElement).classList.contains('keys'),
    replacement: (_c, node) => {
      const keys = Array.from((node as HTMLElement).querySelectorAll('kbd')).map((k) =>
        (Array.from(k.classList).find((c) => c.startsWith('key-'))?.slice(4) ?? k.textContent ?? '').toLowerCase());
      return `++${keys.join('+')}++`;
    },
  });
  t.addRule('taskItem', {
    filter: (node) => node.nodeName === 'INPUT' && (node as HTMLInputElement).type === 'checkbox',
    // The label text that follows already starts with a space.
    replacement: (_c, node) => ((node as HTMLInputElement).checked ? '[x]' : '[ ]'),
  });
  // "- item" rather than turndown's "-   item"; nested lines indent by four spaces.
  t.addRule('listItem', {
    filter: 'li',
    replacement: (content, node) => {
      const parent = node.parentNode as HTMLElement;
      let prefix = '- ';
      if (parent.nodeName === 'OL') {
        const start = Number(parent.getAttribute('start') ?? '1');
        prefix = `${start + Array.prototype.indexOf.call(parent.children, node)}. `;
      }
      const pad = '    '; // Python-Markdown (and Material) nest list content by four spaces.
      const body = content.replace(/^\n+/, '').replace(/\n+$/, '\n').replace(/\n(?=.)/g, '\n' + pad);
      return prefix + body + (node.nextSibling && !/\n$/.test(body) ? '\n' : '');
    },
  });
  t.addRule('mathInline', {
    filter: (node) => node.nodeName === 'SPAN' && (node as HTMLElement).classList.contains('math'),
    replacement: (_c, node) => {
      const tex = (node as HTMLElement).textContent ?? '';
      return tex.startsWith('\\(') ? `$${tex.slice(2, -2)}$` : tex;
    },
  });
  t.keep(['u', 'ins', 'small', 'span'] as unknown as TurndownService.Filter);
  service = t;
  return t;
}

const escapeUrl = (url: string) => (/[\s()]/.test(url) ? `<${url}>` : url);

/** Markdown for an edited block's HTML. */
export function htmlToMarkdown(html: string): string {
  const container = document.createElement('div');
  container.innerHTML = html;
  container.querySelectorAll('a.headerlink').forEach((a) => a.remove());
  return turndown().turndown(container.innerHTML).replace(/\n{3,}/g, '\n\n').trim();
}

/**
 * Block kinds the visual editor edits as rich text. Everything else (admonitions, tabs, code,
 * diagrams, grids, raw HTML, math) is edited through its own form or as markdown source, which
 * keeps every Netdocs/Material feature intact.
 */
export const richKinds = new Set(['paragraph', 'heading', 'list', 'quote', 'table']);

/** True when a rich block's HTML contains markup the HTML→markdown step could not restore exactly. */
export function needsSourceEditing(kind: string, source: string, html: string): boolean {
  if (!richKinds.has(kind)) return true;
  // attr_list braces, critic markup, macros, snippets, inline HTML, math, reference-style links
  // and hard line breaks survive only as source.
  const sourceOnly = /\{\s*[:.#][^}]*\}|\{[^}\s]+=[^}]*\}|\{(\+\+|--|~~|==|>>)|\{\{|\{%|--8<--|<\/?[a-zA-Z][^>]*>|\$[^$\s][^$]*\$|\\\(|\]\[[^\]]*\]|\]\s*$| {2}$/m;
  if (sourceOnly.test(source)) return true;
  const probe = document.createElement('div');
  probe.innerHTML = html;
  if (probe.querySelector('script, iframe, video, audio, details, .admonition, .tabbed-set, pre, .grid, svg:not(.twemoji svg), .arithmatex, dl')) return true;
  // A table cell spanning lines or a nested list inside a table cannot round-trip.
  if (kind === 'table' && probe.querySelector('td ul, td ol, td p, th p')) return true;
  return false;
}

export interface InsertTemplate {
  id: string;
  label: string;
  group: string;
  markdown: string;
}

const admonitionTypes = ['note', 'abstract', 'info', 'tip', 'success', 'question', 'warning', 'failure', 'danger', 'bug', 'example', 'quote'];
export { admonitionTypes };

/** Blocks the editor can insert: the features the Netdocs docs advertise. */
export const insertTemplates: InsertTemplate[] = [
  { id: 'p', group: 'Text', label: 'Paragraph', markdown: 'New paragraph.' },
  { id: 'h2', group: 'Text', label: 'Heading 2', markdown: '## Heading' },
  { id: 'h3', group: 'Text', label: 'Heading 3', markdown: '### Heading' },
  { id: 'h4', group: 'Text', label: 'Heading 4', markdown: '#### Heading' },
  { id: 'ul', group: 'Text', label: 'Bulleted list', markdown: '- List item' },
  { id: 'ol', group: 'Text', label: 'Numbered list', markdown: '1. First step' },
  { id: 'task', group: 'Text', label: 'Task list', markdown: '- [ ] To do' },
  { id: 'quote', group: 'Text', label: 'Quote', markdown: '> Quoted text.' },
  { id: 'deflist', group: 'Text', label: 'Definition list', markdown: 'Term\n:   Definition of the term.' },
  { id: 'hr', group: 'Text', label: 'Divider', markdown: '---' },
  { id: 'table', group: 'Text', label: 'Table', markdown: '| Column | Column |\n| ------ | ------ |\n| Cell   | Cell   |' },
  { id: 'image', group: 'Media', label: 'Image', markdown: '![Description](image.png)' },
  { id: 'figure', group: 'Media', label: 'Image with caption', markdown: '^^^\n![Description](image.png){ width="400" }\n^^^ Caption' },
  { id: 'note', group: 'Callouts', label: 'Admonition (note)', markdown: '!!! note "Note"\n\n    Text inside the note.' },
  { id: 'tip', group: 'Callouts', label: 'Admonition (tip)', markdown: '!!! tip\n\n    A helpful suggestion.' },
  { id: 'warning', group: 'Callouts', label: 'Admonition (warning)', markdown: '!!! warning\n\n    Something to be careful about.' },
  { id: 'details', group: 'Callouts', label: 'Collapsible (closed)', markdown: '??? note "Click to expand"\n\n    Hidden until expanded.' },
  { id: 'details-open', group: 'Callouts', label: 'Collapsible (open)', markdown: '???+ note "Expanded by default"\n\n    Shown until collapsed.' },
  { id: 'tabs', group: 'Layout', label: 'Content tabs', markdown: '=== "First tab"\n\n    Content of the first tab.\n\n=== "Second tab"\n\n    Content of the second tab.' },
  { id: 'cards', group: 'Layout', label: 'Card grid', markdown: '<div class="grid cards" markdown>\n\n- :material-rocket-launch: **First card**\n\n    ---\n\n    Card text.\n\n    [:octicons-arrow-right-24: Read more](index.md)\n\n- :material-puzzle: **Second card**\n\n    ---\n\n    Card text.\n\n</div>' },
  { id: 'grid', group: 'Layout', label: 'Plain grid', markdown: '<div class="grid" markdown>\n\nLeft column.\n\nRight column.\n\n</div>' },
  { id: 'code', group: 'Code', label: 'Code block', markdown: '```python title="example.py"\nprint("Hello")\n```' },
  { id: 'code-lines', group: 'Code', label: 'Code with line numbers', markdown: '```python linenums="1" hl_lines="2"\nfirst = 1\nsecond = 2\n```' },
  { id: 'mermaid', group: 'Code', label: 'Mermaid diagram', markdown: '```mermaid\ngraph LR\n  A[Start] --> B{Choice}\n  B -->|Yes| C[Done]\n  B -->|No| A\n```' },
  { id: 'timeline', group: 'Code', label: 'Timeline (timeline plugin)', markdown: '```timeline\ntitle: Project plan\ninputs:\n  - name: start\n    label: Project start\n    default: 01/04/2027\noutputs:\n  - name: kickoff\n    label: Kick-off meeting\n    type: weekdays\n    expr: start + 1\n    duration: 1\n  - name: build\n    label: Build\n    type: weekdays\n    expr: kickoff + 1\n    duration: 10\n```' },
  { id: 'math', group: 'Code', label: 'Math block', markdown: '$$\nE = mc^2\n$$' },
  { id: 'footnote', group: 'Extras', label: 'Footnote definition', markdown: '[^1]: The footnote text.' },
  { id: 'abbr', group: 'Extras', label: 'Abbreviation', markdown: '*[HTML]: HyperText Markup Language' },
  { id: 'button', group: 'Extras', label: 'Button link', markdown: '[Get started](index.md){ .md-button .md-button--primary }' },
  { id: 'html', group: 'Extras', label: 'Raw HTML', markdown: '<div markdown>\n\nHTML block with **markdown** inside.\n\n</div>' },
];

// --- Structured editors -----------------------------------------------------------------------

export interface Admonition { marker: '!!!' | '???' | '???+'; type: string; title: string | null; body: string; extra: string }

/** Parses an admonition block into its parts, or null when the source is not one. */
export function parseAdmonition(source: string): Admonition | null {
  const lines = source.split('\n');
  const m = /^(!!!|\?\?\?\+?)\s+([^\s"]+)((?:\s+[^\s"]+)*)\s*(?:"([^"]*)")?\s*$/.exec(lines[0]);
  if (!m) return null;
  const body = lines.slice(1).map((l) => (l.startsWith('    ') ? l.slice(4) : l.startsWith('\t') ? l.slice(1) : l)).join('\n').replace(/^\n+/, '');
  return { marker: m[1] as Admonition['marker'], type: m[2], extra: (m[3] ?? '').trim(), title: m[4] ?? null, body };
}

export function formatAdmonition(a: Admonition): string {
  const head = `${a.marker} ${a.type}${a.extra ? ' ' + a.extra : ''}${a.title !== null ? ` "${a.title.replace(/"/g, "'")}"` : ''}`;
  const body = a.body.trim().split('\n').map((l) => (l.length ? '    ' + l : '')).join('\n');
  return body ? `${head}\n\n${body}` : head;
}

export interface CodeFence { fence: string; lang: string; options: string; code: string }

export function parseFence(source: string): CodeFence | null {
  const lines = source.split('\n');
  const m = /^(`{3,}|~{3,})\s*([\w+#.-]*)\s*(.*)$/.exec(lines[0]);
  if (!m || lines.length < 2 || !lines[lines.length - 1].trim().startsWith(m[1])) return null;
  return { fence: m[1], lang: m[2], options: m[3].trim(), code: lines.slice(1, -1).join('\n') };
}

export function formatFence(c: CodeFence): string {
  let fence = c.fence;
  while (c.code.includes(fence)) fence += fence[0];
  return `${fence}${c.lang}${c.options ? ' ' + c.options : ''}\n${c.code}\n${fence}`;
}

export interface Tab { title: string; body: string; selected: boolean }

export function parseTabs(source: string): Tab[] | null {
  const tabs: Tab[] = [];
  let current: Tab | null = null;
  for (const line of source.split('\n')) {
    const m = /^===(!|\+)?\s+"([^"]*)"\s*$/.exec(line);
    if (m) {
      current = { title: m[2], body: '', selected: m[1] === '+' };
      tabs.push(current);
      continue;
    }
    if (!current) return null;
    current.body += (line.startsWith('    ') ? line.slice(4) : line) + '\n';
  }
  tabs.forEach((t) => (t.body = t.body.replace(/^\n+/, '').trimEnd()));
  return tabs.length ? tabs : null;
}

export function formatTabs(tabs: Tab[]): string {
  return tabs.map((t) => {
    const body = t.body.trim().split('\n').map((l) => (l.length ? '    ' + l : '')).join('\n');
    return `===${t.selected ? '+' : ''} "${t.title.replace(/"/g, "'")}"\n\n${body}`;
  }).join('\n\n');
}

/**
 * Languages the code block editor suggests: what highlight.js (the default highlighter) colors,
 * plus Mermaid diagrams. Any other name still works; it is just shown without colors.
 */
export const codeLanguages: [string, string][] = [
  ['text', 'Plain text'], ['bash', 'Bash / shell'], ['sh', 'Shell'], ['shell', 'Shell session'], ['console', 'Console output'],
  ['powershell', 'PowerShell'], ['bat', 'Windows batch'], ['python', 'Python'], ['py', 'Python'], ['javascript', 'JavaScript'],
  ['js', 'JavaScript'], ['typescript', 'TypeScript'], ['ts', 'TypeScript'], ['jsx', 'JSX'], ['tsx', 'TSX'], ['json', 'JSON'],
  ['jsonc', 'JSON with comments'], ['yaml', 'YAML'], ['yml', 'YAML'], ['toml', 'TOML'], ['ini', 'INI / config'], ['xml', 'XML'],
  ['html', 'HTML'], ['css', 'CSS'], ['scss', 'SCSS'], ['less', 'Less'], ['markdown', 'Markdown'], ['md', 'Markdown'],
  ['csharp', 'C#'], ['cs', 'C#'], ['fsharp', 'F#'], ['vbnet', 'VB.NET'], ['java', 'Java'], ['kotlin', 'Kotlin'], ['scala', 'Scala'],
  ['groovy', 'Groovy'], ['go', 'Go'], ['rust', 'Rust'], ['c', 'C'], ['cpp', 'C++'], ['objectivec', 'Objective-C'], ['swift', 'Swift'],
  ['php', 'PHP'], ['ruby', 'Ruby'], ['perl', 'Perl'], ['lua', 'Lua'], ['r', 'R'], ['julia', 'Julia'], ['dart', 'Dart'],
  ['elixir', 'Elixir'], ['erlang', 'Erlang'], ['haskell', 'Haskell'], ['clojure', 'Clojure'], ['ocaml', 'OCaml'], ['zig', 'Zig'],
  ['sql', 'SQL'], ['pgsql', 'PostgreSQL'], ['graphql', 'GraphQL'], ['dockerfile', 'Dockerfile'], ['makefile', 'Makefile'],
  ['cmake', 'CMake'], ['nginx', 'nginx config'], ['apache', 'Apache config'], ['properties', 'Java properties'],
  ['diff', 'Diff / patch'], ['plaintext', 'Plain text'], ['latex', 'LaTeX'], ['protobuf', 'Protocol Buffers'],
  ['terraform', 'Terraform / HCL'], ['hcl', 'HCL'], ['nix', 'Nix'], ['arduino', 'Arduino'], ['armasm', 'ARM assembly'],
  ['x86asm', 'x86 assembly'], ['vhdl', 'VHDL'], ['verilog', 'Verilog'], ['matlab', 'MATLAB'], ['wasm', 'WebAssembly'],
  ['http', 'HTTP'], ['csv', 'CSV'], ['jinja', 'Jinja / templates'], ['twig', 'Twig'], ['handlebars', 'Handlebars'],
  ['vim', 'Vim script'], ['awk', 'AWK'], ['mermaid', 'Mermaid diagram'],
];

// --- Timeline plugin blocks ----------------------------------------------------------------------

/** One `inputs[]` or `outputs[]` entry. Keys are kept in their written order, unknown ones too. */
export type TimelineItem = Record<string, string>;
/** One `exclusions[]` entry: a single date (`to` empty) or an inclusive range. */
export interface TimelineExclusion { from: string; to: string }
/** A ```` ```timeline ```` block. Top-level values are strings; empty means the key is left out. */
export interface Timeline {
  settings: Record<string, string>;
  exclusions: TimelineExclusion[];
  inputs: TimelineItem[];
  outputs: TimelineItem[];
}

export const timelineSettings = ['title', 'date_format', 'show_summary', 'display_date_format', 'edit_exclusions'];
const timelineLists = ['exclusions', 'inputs', 'outputs'];

function yamlScalar(raw: string): string {
  let v = raw.trim();
  if (v.startsWith('"')) {
    const end = v.lastIndexOf('"');
    try { return JSON.parse(v.slice(0, end + 1)) as string; } catch { return v.slice(1, end); }
  }
  if (v.startsWith("'")) return v.slice(1, v.lastIndexOf("'")).replace(/''/g, "'");
  const hash = v.search(/\s#/);
  if (hash >= 0) v = v.slice(0, hash).trim();
  return v === '~' || v === 'null' ? '' : v;
}

function yamlValue(v: string): string {
  if (v === '' || /^[\s]|[\s]$|^[-?:,[\]{}#&*!|>'"%@`]|: |\s#|^(true|false|yes|no|on|off|null|~)$/i.test(v) || /[,[\]{}]/.test(v)) return JSON.stringify(v);
  return v;
}

/** `{ from: a, to: b }`, or null when the text is not a flow map of plain keys. */
function flowMap(text: string): Record<string, string> | null {
  const m = /^\{(.*)\}\s*(#.*)?$/.exec(text.trim());
  if (!m) return null;
  const map: Record<string, string> = {};
  for (const part of m[1].split(',')) {
    if (!part.trim()) continue;
    const kv = /^\s*([\w-]+)\s*:\s*(.*)$/.exec(part);
    if (!kv) return null;
    map[kv[1]] = yamlScalar(kv[2]);
  }
  return map;
}

/**
 * Parses the YAML of a timeline block, or returns null for anything this form can't edit without
 * losing it (other top-level keys, nested values, multi-line strings). Comments are not kept.
 */
export function parseTimeline(code: string): Timeline | null {
  const t: Timeline = { settings: {}, exclusions: [], inputs: [], outputs: [] };
  let list: string | null = null;
  let item: TimelineItem | null = null;
  let itemIndent = 0;
  for (const rawLine of code.split('\n')) {
    const line = rawLine.replace(/\s+$/, '');
    if (!line.trim() || /^\s*#/.test(line)) continue;
    const indent = line.length - line.trimStart().length;
    if (indent === 0) {
      const m = /^([\w-]+):\s*(.*)$/.exec(line);
      if (!m) return null;
      const [, key, rest] = m;
      item = null;
      if (timelineLists.includes(key)) {
        const v = yamlScalar(rest);
        if (v && v !== '[]') return null;
        list = key;
        continue;
      }
      if (!timelineSettings.includes(key) || /^[|>[{]/.test(rest.trim())) return null;
      list = null;
      t.settings[key] = yamlScalar(rest);
      continue;
    }
    if (!list) return null;
    const dash = /^(\s*)-\s+(.*)$/.exec(line);
    if (dash) {
      const body = dash[2];
      itemIndent = dash[1].length + 2;
      if (list === 'exclusions') {
        item = null;
        const fm = flowMap(body);
        if (fm) {
          if (Object.keys(fm).some((k) => k !== 'from' && k !== 'to')) return null;
          t.exclusions.push({ from: fm.from ?? '', to: fm.to ?? '' });
          continue;
        }
        const kv = /^(from|to):\s*(.*)$/.exec(body);
        if (kv) {
          const ex = { from: '', to: '' };
          ex[kv[1] as 'from' | 'to'] = yamlScalar(kv[2]);
          t.exclusions.push(ex);
          item = ex as unknown as TimelineItem;
          continue;
        }
        if (/^[[{|>]/.test(body)) return null;
        t.exclusions.push({ from: yamlScalar(body), to: '' });
        continue;
      }
      const fm = flowMap(body);
      item = {};
      (list === 'inputs' ? t.inputs : t.outputs).push(item);
      if (fm) { Object.assign(item, fm); item = null; continue; }
      const kv = /^([\w-]+):\s*(.*)$/.exec(body);
      if (!kv || /^[|>[{]/.test(kv[2].trim())) return null;
      item[kv[1]] = yamlScalar(kv[2]);
      continue;
    }
    const kv = /^\s+([\w-]+):\s*(.*)$/.exec(line);
    if (!item || !kv || indent !== itemIndent || /^[|>[{]/.test(kv[2].trim())) return null;
    if (list === 'exclusions' && kv[1] !== 'from' && kv[1] !== 'to') return null;
    item[kv[1]] = yamlScalar(kv[2]);
  }
  return t;
}

export function formatTimeline(t: Timeline): string {
  const lines: string[] = [];
  for (const key of timelineSettings) {
    const v = t.settings[key];
    if (v !== undefined && v !== '') lines.push(`${key}: ${key === 'show_summary' || key === 'edit_exclusions' ? v : yamlValue(v)}`);
  }
  if (t.exclusions.length) {
    lines.push('exclusions:');
    for (const e of t.exclusions) {
      lines.push(e.to.trim() ? `  - { from: ${yamlValue(e.from.trim())}, to: ${yamlValue(e.to.trim())} }` : `  - ${yamlValue(e.from.trim())}`);
    }
  }
  for (const [key, items] of [['inputs', t.inputs], ['outputs', t.outputs]] as const) {
    lines.push(`${key}:`);
    for (const item of items) {
      const entries = Object.entries(item).filter(([, v]) => v !== '');
      if (!entries.length) continue;
      entries.forEach(([k, v], i) => lines.push(`${i ? '    ' : '  - '}${k}: ${k === 'editable' || k === 'duration' ? v : yamlValue(v)}`));
    }
  }
  return lines.join('\n');
}
