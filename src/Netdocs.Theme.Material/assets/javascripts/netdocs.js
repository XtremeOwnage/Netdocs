(function () {
  "use strict";

  const base = document.body.getAttribute("data-base") || "";

  // Code copy buttons.
  document.querySelectorAll(".highlight").forEach((block) => {
    const btn = document.createElement("button");
    btn.className = "md-clipboard";
    btn.type = "button";
    btn.textContent = "Copy";
    btn.addEventListener("click", () => {
      const code = block.querySelector("code");
      if (!code) return;
      navigator.clipboard.writeText(code.innerText).then(() => {
        btn.textContent = "Copied";
        setTimeout(() => (btn.textContent = "Copy"), 1500);
      });
    });
    block.appendChild(btn);
  });

  // Lightweight client-side search over the Material-schema index.
  const input = document.querySelector("[data-md-component='search-query']");
  const output = document.querySelector("[data-md-component='search-output']");
  if (!input || !output) return;

  let docs = null;
  let loading = false;

  function load() {
    if (docs || loading) return Promise.resolve();
    loading = true;
    return fetch(base + "search/search_index.json")
      .then((r) => r.json())
      .then((data) => { docs = data.docs || []; })
      .catch(() => { docs = []; });
  }

  function escapeHtml(s) {
    return s.replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
  }

  function search(query) {
    const terms = query.toLowerCase().split(/\s+/).filter(Boolean);
    if (terms.length === 0) return [];
    const results = [];
    for (const doc of docs) {
      const hay = (doc.title + " " + doc.text).toLowerCase();
      let score = 0;
      for (const t of terms) {
        const idx = hay.indexOf(t);
        if (idx === -1) { score = 0; break; }
        score += doc.title.toLowerCase().includes(t) ? 10 : 1;
      }
      if (score > 0) results.push({ doc, score });
    }
    results.sort((a, b) => b.score - a.score);
    return results.slice(0, 15);
  }

  function render(results, query) {
    if (results.length === 0) {
      output.innerHTML = "<div class='md-search-result__item'>No results.</div>";
    } else {
      output.innerHTML = results.map(({ doc }) => {
        const teaser = escapeHtml((doc.text || "").slice(0, 140));
        return `<div class="md-search-result__item">
          <a href="${base}${doc.location}">${escapeHtml(doc.title)}</a>
          <div class="md-search-result__teaser">${teaser}…</div>
        </div>`;
      }).join("");
    }
    output.classList.add("md-search__output--open");
  }

  let timer;
  input.addEventListener("input", () => {
    const q = input.value.trim();
    clearTimeout(timer);
    if (!q) { output.classList.remove("md-search__output--open"); return; }
    timer = setTimeout(() => load().then(() => render(search(q), q)), 120);
  });

  document.addEventListener("click", (e) => {
    if (!output.contains(e.target) && e.target !== input)
      output.classList.remove("md-search__output--open");
  });
})();
