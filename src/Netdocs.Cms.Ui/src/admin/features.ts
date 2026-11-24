// What each theme feature flag does, with a link to the Material for MkDocs documentation that
// Netdocs follows for these flags.
const base = 'https://squidfunk.github.io/mkdocs-material/';

export const featureHelp: Record<string, { text: string; doc: string }> = {
  'announce.dismiss': { text: 'Adds a close button to the announcement bar; once closed it stays hidden for that visitor.', doc: 'setup/setting-up-the-header/#mark-as-read' },
  'content.action.edit': { text: 'Shows an "Edit this page" button that opens the page source in your repository.', doc: 'setup/adding-a-git-repository/#code-actions' },
  'content.action.view': { text: 'Shows a "View source" button that opens the raw page source in your repository.', doc: 'setup/adding-a-git-repository/#code-actions' },
  'content.code.annotate': { text: 'Turns (1) markers in code comments into clickable annotations.', doc: 'reference/code-blocks/#code-annotations' },
  'content.code.copy': { text: 'Adds a copy-to-clipboard button to every code block.', doc: 'reference/code-blocks/#code-copy-button' },
  'content.code.select': { text: 'Adds a button for selecting line ranges in code blocks, so readers can link to them.', doc: 'reference/code-blocks/#code-selection-button' },
  'content.footnote.tooltips': { text: 'Shows footnotes in a tooltip on hover instead of jumping to the bottom of the page.', doc: 'reference/footnotes/#footnote-tooltips' },
  'content.tabs.link': { text: 'Links content tabs with the same label: picking "Linux" in one tab set picks it everywhere.', doc: 'reference/content-tabs/#linked-content-tabs' },
  'content.tooltips': { text: 'Replaces the browser\'s plain title tooltips with styled ones for links, abbreviations and icons.', doc: 'reference/tooltips/#improved-tooltips' },
  'header.autohide': { text: 'Hides the header while scrolling down and brings it back when scrolling up.', doc: 'setup/setting-up-the-header/#automatic-hiding' },
  'navigation.expand': { text: 'Expands every section of the left navigation by default.', doc: 'setup/setting-up-navigation/#navigation-expansion' },
  'navigation.footer': { text: 'Shows "Previous" and "Next" page links in the footer.', doc: 'setup/setting-up-the-footer/#navigation' },
  'navigation.indexes': { text: 'Lets a section have its own page: an index.md inside a folder becomes the section\'s landing page.', doc: 'setup/setting-up-navigation/#section-index-pages' },
  'navigation.instant': { text: 'Loads pages without a full browser reload, like a single-page app. Faster page changes.', doc: 'setup/setting-up-navigation/#instant-loading' },
  'navigation.instant.prefetch': { text: 'With instant loading: starts loading a page when the pointer hovers its link.', doc: 'setup/setting-up-navigation/#instant-prefetching' },
  'navigation.instant.progress': { text: 'With instant loading: shows a progress bar at the top while a slow page loads.', doc: 'setup/setting-up-navigation/#progress-indicator' },
  'navigation.path': { text: 'Shows breadcrumbs (Section › Subsection) above the page title.', doc: 'setup/setting-up-navigation/#navigation-path' },
  'navigation.prune': { text: 'Only includes the visible part of the navigation in each page, which keeps large sites smaller.', doc: 'setup/setting-up-navigation/#navigation-pruning' },
  'navigation.sections': { text: 'Shows top-level sections as headed groups in the sidebar instead of collapsible items.', doc: 'setup/setting-up-navigation/#navigation-sections' },
  'navigation.tabs': { text: 'Shows the top-level sections as tabs under the header on wide screens.', doc: 'setup/setting-up-navigation/#navigation-tabs' },
  'navigation.tabs.sticky': { text: 'Keeps the navigation tabs visible while scrolling.', doc: 'setup/setting-up-navigation/#sticky-navigation-tabs' },
  'navigation.top': { text: 'Shows a "Back to top" button after scrolling down.', doc: 'setup/setting-up-navigation/#back-to-top-button' },
  'navigation.tracking': { text: 'Updates the address bar with the heading currently in view, so copied links point to that spot.', doc: 'setup/setting-up-navigation/#anchor-tracking' },
  'search.highlight': { text: 'Highlights the search words on the page you open from the results.', doc: 'setup/setting-up-site-search/#search-highlighting' },
  'search.share': { text: 'Adds a button that copies a link to the current search.', doc: 'setup/setting-up-site-search/#search-sharing' },
  'search.suggest': { text: 'Suggests the rest of the word while typing in search.', doc: 'setup/setting-up-site-search/#search-suggestions' },
  'toc.follow': { text: 'Scrolls the table of contents along with the page so the current heading stays visible.', doc: 'setup/setting-up-navigation/#anchor-following' },
  'toc.integrate': { text: 'Puts the table of contents inside the left navigation instead of a right sidebar.', doc: 'setup/setting-up-navigation/#navigation-integration' },
};

export const featureDocUrl = (doc: string) => base + doc;
