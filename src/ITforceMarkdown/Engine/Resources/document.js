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
window.__setContent = (html) => { doc.innerHTML = html; };
window.__scrollToHeading = (id) => scrollToHeading(id);

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
