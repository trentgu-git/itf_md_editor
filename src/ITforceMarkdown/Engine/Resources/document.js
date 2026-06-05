// WebView2 hosting side 跟 .NET 通信:
//   - .NET 注入更新 markdown 时, 把 #doc innerHTML 重新设
//   - contenteditable=true 时, input 事件回报 markdown 文本给 host

function notifyChange() {
  if (doc.contentEditable !== 'true') return;
  const markdown = blocksToMarkdown(doc);
  if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
    window.chrome.webview.postMessage({ type: 'editorChanged', markdown });
  }
}

let notifyTimer = null;
doc.addEventListener('input', () => {
  clearTimeout(notifyTimer);
  notifyTimer = setTimeout(notifyChange, 160);
});

function scrollToHeading(id) {
  const el = document.getElementById(id);
  if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
}

// ─── 主机调用接口 (WebView2.ExecuteScriptAsync) ───
window.__setContent = (html) => {
  doc.innerHTML = html;
  // 重设 content 后立刻跑一遍渲染管线 (mermaid / hljs / KaTeX).
  // 跟 host 注入新 markdown 时一致.
  window.__renderEnhancements();
};
window.__scrollToHeading = (id) => scrollToHeading(id);

// ─── Tier 1 渲染增强 (mermaid / hljs / KaTeX / 图片点击放大) ───
//
// 这些库通过 MarkdownEngine.DocumentHtml 把 <script>/<style> 标签注入,
// 所以这里只要调用对应的 init/run 方法.

window.__renderEnhancements = function () {
  // 1) Mermaid: div.mermaid 源码 → SVG
  if (window.mermaid && window.mermaid.run) {
    const nodes = doc.querySelectorAll('div.mermaid:not([data-processed="true"])');
    if (nodes.length) {
      try {
        const isDark =
          document.documentElement.classList.contains('force-dark') ||
          (!document.documentElement.classList.contains('force-light') &&
            window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches);
        window.mermaid.initialize({
          startOnLoad: false,
          theme: isDark ? 'dark' : 'default',
          securityLevel: 'loose',
        });
        window.mermaid.run({ nodes });
      } catch (e) { console.error('mermaid run failed:', e); }
    }
  }

  // 2) highlight.js: 给 <pre><code class="language-xxx"> 着色
  if (window.hljs && typeof window.hljs.highlightElement === 'function') {
    try {
      doc.querySelectorAll('pre code').forEach(function (block) {
        if (block.dataset.highlighted) return;  // 避免重复着色
        window.hljs.highlightElement(block);
      });
    } catch (e) { console.error('hljs failed:', e); }
  }

  // 3) KaTeX: 自动渲染 $...$ / $$...$$
  if (window.renderMathInElement) {
    try {
      window.renderMathInElement(doc, {
        delimiters: [
          { left: '$$', right: '$$', display: true  },
          { left: '\\[', right: '\\]', display: true  },
          { left: '$',  right: '$',  display: false },
          { left: '\\(', right: '\\)', display: false },
        ],
        throwOnError: false,
        ignoredTags: ['script', 'noscript', 'style', 'textarea', 'pre', 'code'],
      });
    } catch (e) { console.error('KaTeX failed:', e); }
  }
};

// 首次加载就跑一次 (HTML body 已经有 doc.innerHTML)
window.__renderEnhancements();

// ─── 图片 / mermaid 点击放大 (event delegation) ───
//
// 用 mousedown + capture 抢在 contenteditable cursor placement 之前.
doc.addEventListener('mousedown', function (ev) {
  const target = ev.target;
  if (!target || !target.closest) return;
  // 1) mermaid 图 → 弹 SVG
  const md = target.closest('div.mermaid');
  if (md) {
    const svg = md.querySelector('svg');
    if (!svg) return;  // mermaid 还没渲染好就不弹空 modal
    ev.preventDefault();
    ev.stopPropagation();
    window.__openZoomModal(svg.outerHTML);
    return;
  }
  // 2) 普通 <img> → 弹原图
  if (target.tagName === 'IMG' && !target.classList.contains('no-zoom')) {
    ev.preventDefault();
    ev.stopPropagation();
    window.__openZoomModal('<img src="' + target.src + '" alt="' + (target.alt || '') + '">');
    return;
  }
}, true);

window.__openZoomModal = function (innerHtml) {
  window.__closeZoomModal();
  const overlay = document.createElement('div');
  overlay.id = '__mermaid_overlay';
  overlay.innerHTML = innerHtml;
  const close = document.createElement('div');
  close.id = '__mermaid_overlay_close';
  close.textContent = '×';
  overlay.appendChild(close);
  overlay.addEventListener('click', window.__closeZoomModal);
  document.body.appendChild(overlay);
  window.__zoomEscHandler = (ev) => {
    if (ev.key === 'Escape') window.__closeZoomModal();
  };
  document.addEventListener('keydown', window.__zoomEscHandler);
};
window.__closeZoomModal = function () {
  const ov = document.getElementById('__mermaid_overlay');
  if (ov) ov.remove();
  if (window.__zoomEscHandler) {
    document.removeEventListener('keydown', window.__zoomEscHandler);
    window.__zoomEscHandler = null;
  }
};

// 富文本格式化命令 — 工具栏按钮通过 ExecuteScriptAsync 调这些。
// execCommand 虽然被 W3C 标记为 deprecated, 但在所有 Chromium 衍生品里
// (包括 WebView2) 仍然是处理 contenteditable 富文本编辑的事实标准, 短期内
// 不会移除。
window.__runCommand = function (command, value) {
  doc.focus();
  try { document.execCommand(command, false, value === undefined ? null : value); }
  catch (e) { /* ignore */ }
  notifyChange();
};

window.__formatBlock = function (tagName) {
  doc.focus();
  try { document.execCommand('formatBlock', false, tagName); }
  catch (e) { /* ignore */ }
  notifyChange();
};

window.__insertLink = function (url) {
  if (!url) return;
  window.__runCommand('createLink', url);
};

window.__insertImage = function (url) {
  if (!url) return;
  window.__runCommand('insertImage', url);
};

window.__insertHTML = function (html) {
  doc.focus();
  try { document.execCommand('insertHTML', false, html); }
  catch (e) { /* ignore */ }
  notifyChange();
};

window.__insertTable = function () {
  window.__insertHTML(
    '<table><thead><tr><th>Column</th><th>Column</th></tr></thead>' +
    '<tbody><tr><td>Value</td><td>Value</td></tr></tbody></table><p><br></p>');
};

window.__insertHR = function () {
  window.__runCommand('insertHorizontalRule');
};

window.__insertCodeBlock = function () {
  window.__insertHTML('<pre><code>code</code></pre><p><br></p>');
};

// 富文本 -> Markdown 反向序列化 (简化版, 跟 Mac 版 blocksToMarkdown 等价)
function blocksToMarkdown(root) {
  const parts = [];
  root.childNodes.forEach(node => {
    const block = blockMarkdown(node).replace(/[ \t]+$/gm, '');
    if (block.trim().length > 0) parts.push(block);
  });
  return parts.join('\n\n').replace(/\n{3,}/g, '\n\n').trim() + '\n';
}

function blockMarkdown(node) {
  if (node.nodeType === Node.TEXT_NODE) {
    const text = node.textContent;
    return text.trim().length > 0 ? text.replace(/\s+/g, ' ').trim() : '';
  }
  if (node.nodeType !== Node.ELEMENT_NODE) return '';
  const tag = node.tagName.toLowerCase();
  if (/^h[1-6]$/.test(tag)) return '#'.repeat(Number(tag[1])) + ' ' + inlineMarkdown(node);
  if (tag === 'p' || tag === 'div') return inlineMarkdown(node);
  if (tag === 'blockquote') {
    const inner = Array.from(node.childNodes).map(blockMarkdown).filter(Boolean).join('\n\n');
    return inner.split('\n').map(line => '> ' + line).join('\n');
  }
  if (tag === 'pre') {
    const inner = node.querySelector('code');
    let lang = '';
    if (inner) {
      const cls = inner.getAttribute('class') || '';
      const m = cls.match(/language-([\w+\-.]+)/);
      if (m) lang = m[1];
    }
    const body = (inner ? inner.textContent : node.textContent).replace(/\n$/, '');
    return '```' + lang + '\n' + body + '\n```';
  }
  if (tag === 'ul' || tag === 'ol') return listMarkdown(node, 0);
  if (tag === 'table') return tableMarkdown(node);
  if (tag === 'hr') return '---';
  return inlineMarkdown(node);
}

function listMarkdown(list, depth) {
  const ordered = list.tagName.toLowerCase() === 'ol';
  const indent = '  '.repeat(depth);
  const items = Array.from(list.children).filter(c => c.tagName.toLowerCase() === 'li');
  return items.map((li, i) => {
    let marker = ordered ? (i + 1) + '. ' : '- ';
    const checkbox = li.querySelector(':scope > input[type="checkbox"]');
    let prefix = '';
    if (checkbox) prefix = checkbox.checked ? '[x] ' : '[ ] ';
    const inlineNodes = [];
    const nestedLists = [];
    li.childNodes.forEach(child => {
      if (child.nodeType === Node.ELEMENT_NODE) {
        const tn = child.tagName.toLowerCase();
        if (tn === 'ul' || tn === 'ol') { nestedLists.push(child); return; }
        if (child === checkbox) return;
      }
      inlineNodes.push(child);
    });
    const inlineText = inlineNodes.map(n => {
      if (n.nodeType === Node.TEXT_NODE) return n.textContent.replace(/\s+/g, ' ');
      if (n.nodeType === Node.ELEMENT_NODE) return inlineMarkdown(n);
      return '';
    }).join('').trim();
    const head = indent + marker + prefix + inlineText;
    const tail = nestedLists.map(child => listMarkdown(child, depth + 1)).join('\n');
    return tail ? head + '\n' + tail : head;
  }).join('\n');
}

function inlineMarkdown(node) {
  let out = '';
  node.childNodes.forEach(child => {
    if (child.nodeType === Node.TEXT_NODE) {
      out += child.textContent.replace(/\s+/g, ' ');
      return;
    }
    if (child.nodeType !== Node.ELEMENT_NODE) return;
    const tag = child.tagName.toLowerCase();
    const text = inlineMarkdown(child);
    if (tag === 'strong' || tag === 'b') out += '**' + text + '**';
    else if (tag === 'em' || tag === 'i') out += '*' + text + '*';
    else if (tag === 's' || tag === 'del' || tag === 'strike') out += '~~' + text + '~~';
    else if (tag === 'code') out += '`' + child.textContent + '`';
    else if (tag === 'a') out += '[' + text + '](' + (child.getAttribute('href') || '') + ')';
    else if (tag === 'img') out += '![' + (child.getAttribute('alt') || '') + '](' + (child.getAttribute('src') || '') + ')';
    else if (tag === 'br') out += '\n';
    else out += text;
  });
  return out.replace(/[ \t]{2,}/g, ' ');
}

function tableMarkdown(table) {
  const rows = Array.from(table.querySelectorAll('tr')).map(row =>
    Array.from(row.children).map(cell => inlineMarkdown(cell).replace(/\|/g, '\\|'))
  );
  if (!rows.length) return '';
  const width = Math.max(...rows.map(r => r.length));
  const normalized = rows.map(r => [...r, ...Array(Math.max(0, width - r.length)).fill('')]);
  const header = '| ' + normalized[0].join(' | ') + ' |';
  const divider = '| ' + normalized[0].map(() => '---').join(' | ') + ' |';
  const body = normalized.slice(1).map(r => '| ' + r.join(' | ') + ' |').join('\n');
  return [header, divider, body].filter(Boolean).join('\n');
}
