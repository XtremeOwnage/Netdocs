// What the Plugins screen knows about each built-in plugin: a one-line summary, its docs page,
// and the options it reads (so they can be edited as a form instead of raw JSON).

export type OptionType = 'bool' | 'int' | 'string' | 'select' | 'list' | 'color';

export interface OptionSpec {
  key: string;
  label: string;
  type: OptionType;
  /** The value the plugin uses when the option is not set. */
  default?: boolean | number | string;
  hint?: string;
  choices?: string[];
  /** Nested object the option lives in, such as social's cards_layout_options. */
  group?: string;
}

export interface PluginInfo {
  name: string;
  label: string;
  summary: string;
  doc: string;
  options: OptionSpec[];
  /** The plugin has its own settings editor on the screen. */
  extra?: 'abbreviations' | 'blog' | 'file-filter' | 'social';
}

const DOCS = 'https://xtremeownage.github.io/Netdocs/plugins/';
export const pluginDocUrl = (p: PluginInfo) => DOCS + p.doc + '/';

const layout = 'cards_layout_options';

export const pluginCatalog: PluginInfo[] = [
  {
    name: 'abbreviations', label: 'Abbreviations', doc: 'abbreviations', extra: 'abbreviations',
    summary: 'Adds hover tooltips for abbreviations (HTML, CSS…) on every page from a shared list of definitions.',
    options: [
      { key: 'files', label: 'Definition files', type: 'list', hint: 'Files in the docs folder that hold the definitions. Default: _include/abbv.md' },
      { key: 'first_instance_only', label: 'Only the first use on each page', type: 'bool', default: true, hint: 'Off marks every use of a term.' },
    ],
  },
  {
    name: 'arithmatex', label: 'Math (Arithmatex)', doc: 'arithmatex',
    summary: 'Typesets LaTeX math with MathJax.',
    options: [{ key: 'mathjax_url', label: 'MathJax URL', type: 'string', hint: 'Defaults to the MathJax 3 CDN. Point at a local copy for offline builds.' }],
  },
  {
    name: 'b64', label: 'Embed images (b64)', doc: 'b64',
    summary: 'Embeds local images into the page as data: URIs so a page is self-contained.',
    options: [
      { key: 'default', label: 'Embed on every page', type: 'bool', default: false, hint: 'Off: only pages that ask for it.' },
      { key: 'base_path', label: 'Extra image folders', type: 'list', hint: 'Relative to the project root.' },
    ],
  },
  {
    name: 'blog', label: 'Blog', doc: 'blog', extra: 'blog',
    summary: 'Turns posts under the blog folder into a blog with index, category, archive and author pages.',
    options: [
      { key: 'blog_dir', label: 'Blog folder', type: 'string', default: 'blog/', hint: 'Inside the docs folder. Posts go in its posts/ folder.' },
      { key: 'post_url_date_format', label: 'Date in post URLs', type: 'string', default: 'yyyy', hint: '.NET date format, such as yyyy or yyyy/MM.' },
      { key: 'pagination_per_page', label: 'Posts per index page', type: 'int', default: 20 },
      { key: 'categories', label: 'Category pages', type: 'bool', default: true },
      { key: 'archive', label: 'Archive pages (by year)', type: 'bool', default: true },
    ],
  },
  {
    name: 'calculator', label: 'Calculator', doc: 'calculator',
    summary: 'Interactive calculators written in Markdown with ```calc blocks.', options: [],
  },
  {
    name: 'file-filter', label: 'File filter', doc: 'file-filter', extra: 'file-filter',
    summary: 'Hides pages by front-matter labels or .mkdocsignore paths, for example to keep drafts out of production builds.',
    options: [],
  },
  {
    name: 'git-revision-date-localized', label: 'Git revision dates', doc: 'git-revision-date',
    summary: "Sets each page's updated date (and optionally its created date) from git history.",
    options: [{ key: 'enable_creation_date', label: 'Also set the created date', type: 'bool', default: false, hint: 'From the first commit of the file.' }],
  },
  { name: 'glightbox', label: 'Image lightbox', doc: 'glightbox', summary: 'Opens images in a zoomable lightbox when clicked.', options: [] },
  {
    name: 'imported-docs', label: 'Imported docs', doc: 'imported-docs',
    summary: 'Pulls documentation in from other git repositories or S3 buckets. Its sources are edited as JSON below.',
    options: [{ key: 'pushedDocsDir', label: 'Pushed docs folder', type: 'string', default: 'imported' }],
  },
  { name: 'link-notes', label: 'Link notes', doc: 'link-notes', summary: 'Attaches a note (such as an affiliate disclosure) to links that match rules. Rules are edited as JSON below.', options: [] },
  { name: 'macros', label: 'Macros', doc: 'macros', summary: 'Variables and macros inside Markdown, in the style of mkdocs-macros.', options: [] },
  {
    name: 'meta', label: 'Folder defaults (meta)', doc: 'meta',
    summary: 'Applies front-matter defaults to every page in a folder from a .meta.yml file.',
    options: [{ key: 'meta_file', label: 'File name', type: 'string', default: '.meta.yml' }],
  },
  { name: 'redirects', label: 'Redirects', doc: 'redirects', summary: 'Makes small redirect pages from old URLs to new ones. Maps are edited as JSON below.', options: [] },
  {
    name: 'rss', label: 'RSS feed', doc: 'rss',
    summary: 'Publishes an RSS (and optionally Atom) feed of the blog posts. Needs the blog plugin.',
    options: [
      { key: 'length', label: 'Posts in the feed', type: 'int', default: 20 },
      { key: 'full_content', label: 'Full post content', type: 'bool', default: false, hint: 'Off: only the excerpt.' },
      { key: 'feed_title', label: 'Feed title', type: 'string', hint: 'Defaults to the site name.' },
      { key: 'feed_description', label: 'Feed description', type: 'string', hint: 'Defaults to the site description.' },
      { key: 'image', label: 'Feed image', type: 'string', hint: 'Logo URL. Relative URLs use the site URL.' },
      { key: 'ttl', label: 'Cache time (minutes)', type: 'int', hint: 'Advisory, for feed readers.' },
      { key: 'rss_file', label: 'RSS file name', type: 'string', default: 'feed_rss_created.xml' },
      { key: 'atom', label: 'Also publish an Atom feed', type: 'bool', default: false },
      { key: 'atom_file', label: 'Atom file name', type: 'string', default: 'feed_atom_created.xml' },
      { key: 'social_icon', label: 'Show a feed icon in the footer', type: 'bool', default: false },
      { key: 'social_feed', label: 'Icon links to', type: 'select', choices: ['rss', 'atom'], default: 'rss' },
    ],
  },
  {
    name: 'search', label: 'Search', doc: 'search',
    summary: 'Builds the search index for the search box.',
    options: [
      { key: 'lang', label: 'Language', type: 'string', default: 'en' },
      { key: 'separator', label: 'Word separator (regex)', type: 'string', default: '[\\s\\-]+' },
    ],
  },
  { name: 'snippets', label: 'Snippets', doc: 'snippets', summary: 'Includes other files into Markdown with --8<--.', options: [{ key: 'base_path', label: 'Search folders', type: 'list' }, { key: 'auto_append', label: 'Append to every page', type: 'list' }] },
  {
    name: 'social', label: 'Social cards', doc: 'social', extra: 'social',
    summary: 'Draws an Open Graph image for every page, shown when a link is shared on social sites and chat apps.',
    options: [
      { key: 'cards', label: 'Make cards', type: 'bool', default: true, hint: 'Off stops making cards and removes the og:image tags.' },
      { key: 'enabled_on_serve', label: 'Also while serving', type: 'bool', default: true, hint: 'Off speeds up netdocs serve. Builds still make cards.' },
      { key: 'cache', label: 'Reuse unchanged cards', type: 'bool', default: true },
      { key: 'cards_dir', label: 'Output folder', type: 'string', default: 'assets/social' },
      { key: 'format', label: 'Image format', type: 'select', choices: ['png', 'jpeg', 'webp'], default: 'png' },
      { key: 'quality', label: 'Quality (jpeg/webp)', type: 'int', default: 90 },
      { key: 'width', label: 'Width', type: 'int', default: 1200, group: layout },
      { key: 'height', label: 'Height', type: 'int', default: 630, group: layout },
      { key: 'background_color', label: 'Background', type: 'color', hint: 'Default: the theme primary color.', group: layout },
      { key: 'background_image', label: 'Background image', type: 'string', hint: 'Relative to the project root.', group: layout },
      { key: 'color', label: 'Title color', type: 'color', default: '#f5f5f5', group: layout },
      { key: 'description_color', label: 'Description color', type: 'color', default: '#c9ccd1', group: layout },
      { key: 'accent_color', label: 'Accent bar color', type: 'color', hint: 'Default: the theme accent color.', group: layout },
      { key: 'accent_width', label: 'Accent bar width', type: 'int', default: 12, hint: '0 hides it.', group: layout },
      { key: 'padding', label: 'Padding', type: 'int', default: 70, group: layout },
      { key: 'title_font_size', label: 'Title size', type: 'int', default: 58, group: layout },
      { key: 'description_font_size', label: 'Description size', type: 'int', default: 30, group: layout },
      { key: 'site_name_font_size', label: 'Site name size', type: 'int', default: 28, group: layout },
      { key: 'font_family', label: 'Font family', type: 'string', hint: 'An installed font, such as Roboto.', group: layout },
      { key: 'font_path', label: 'Font file', type: 'string', hint: '.ttf or .otf, relative to the project root. Most reliable in CI.', group: layout },
      { key: 'logo', label: 'Logo', type: 'string', hint: 'Relative to the project root.', group: layout },
      { key: 'logo_size', label: 'Logo size', type: 'int', default: 96, group: layout },
      { key: 'title', label: 'Fixed title', type: 'string', hint: 'Same title on every card. Pages can still override it.', group: layout },
      { key: 'description', label: 'Fixed description', type: 'string', group: layout },
      { key: 'description_length', label: 'Cut description at (characters)', type: 'int', default: 180, group: layout },
    ],
  },
  { name: 'table-reader', label: 'Table reader', doc: 'table-reader', summary: 'Turns {{ read_csv(...) }} into Markdown tables.', options: [] },
  {
    name: 'tags', label: 'Tags', doc: 'tags',
    summary: 'Collects page tags into a tag index and a tags.json export.',
    options: [
      { key: 'export', label: 'Write tags.json', type: 'bool', default: true },
      { key: 'export_file', label: 'Export file name', type: 'string', default: 'tags.json' },
      { key: 'shadow', label: 'Hide shadow tags', type: 'bool', default: false },
      { key: 'shadow_on_serve', label: 'Show shadow tags while serving', type: 'bool', default: true },
      { key: 'shadow_tags_prefix', label: 'Shadow tag prefix', type: 'string', default: '_' },
      { key: 'shadow_tags', label: 'Shadow tags', type: 'list' },
    ],
  },
  { name: 'timeline', label: 'Timeline', doc: 'timeline', summary: 'Interactive project timelines written in Markdown.', options: [] },
  { name: 'typeset', label: 'Smart typography', doc: 'typeset', summary: 'Curly quotes, dashes and ellipses in prose.', options: [] },
  {
    name: 'versioning', label: 'Versions', doc: 'versioning',
    summary: 'Adds a version selector to the header. The version list is edited as JSON below.',
    options: [
      { key: 'provider', label: 'Versions from', type: 'select', choices: ['static', 'git-tags'], default: 'static' },
      { key: 'current', label: 'Current version', type: 'string' },
      { key: 'label', label: 'Selector label', type: 'string', default: 'Version' },
      { key: 'url_template', label: 'URL template', type: 'string', default: '../{version}/' },
      { key: 'tag_pattern', label: 'Tag pattern (git-tags)', type: 'string', default: '^v?\\d+(\\.\\d+)*$' },
    ],
  },
];

/** Other names a plugin answers to in the config. */
export const pluginAliases: Record<string, string> = {
  'pymdownx.snippets': 'snippets',
  'affiliate-links': 'link-notes',
  'pymdownx.b64': 'b64',
  'pymdownx.arithmatex': 'arithmatex',
  calc: 'calculator',
};
