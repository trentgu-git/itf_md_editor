# ITforce Markdown Pro — Windows Port Specification

**Audience**: An AI coding assistant tasked with building a Windows desktop port of the existing macOS app **ITforce Markdown Pro** (v1.1). The macOS source code is not available to the implementer; this document is the single source of truth.

**Goal**: Ship a Windows version that is **feature-parity** with the macOS app for everything in scope (see §3), with native Windows UX where appropriate but **identical Markdown rendering output** (because Export PDF and Print depend on it being pixel-equivalent to the macOS version).

**Tech stack (mandated)**: WPF on .NET 8, WebView2 (Edge Chromium), Markdig (or a 1:1 port of our own engine — see §7), OpenXml SDK for Word export, MSIX packaging.

---

## Table of Contents

1. [Product Overview](#1-product-overview)
2. [Recommended Tech Stack](#2-recommended-tech-stack)
3. [Scope & Non-Goals (v1)](#3-scope--non-goals-v1)
4. [Architecture](#4-architecture)
5. [Data Model](#5-data-model)
6. [Persistence](#6-persistence)
7. [Markdown Engine](#7-markdown-engine)
8. [HTML Templates (CSS / JS)](#8-html-templates-css--js)
9. [UI Specification](#9-ui-specification)
10. [Editor Modes](#10-editor-modes)
11. [File Operations](#11-file-operations)
12. [Export & Print](#12-export--print)
13. [Menus & Keyboard Shortcuts](#13-menus--keyboard-shortcuts)
14. [Window & App Behavior](#14-window--app-behavior)
15. [Visual Design Tokens](#15-visual-design-tokens)
16. [App Lifecycle](#16-app-lifecycle)
17. [Packaging (MSIX)](#17-packaging-msix)
18. [Acceptance Criteria](#18-acceptance-criteria)
19. [Known Limitations & Risks](#19-known-limitations--risks)

---

## 1. Product Overview

**ITforce Markdown Pro for Windows** is a desktop Markdown editor for technical writers, developers, and AI engineers. The user opens one or more local folders as "workspaces", browses `.md` files in a tree, and edits them in three modes: **Read** (rendered HTML), **Edit** (WYSIWYG via `contenteditable`), or **Source** (raw Markdown text).

Documents can be exported to **PDF** or **Word (.docx)** with an A4 page layout, printed via the system print dialog, and opened from the OS Files app or via drag-and-drop. The app maintains a list of recently-opened documents and supports browser-style back/forward navigation between documents in the current session.

The primary differentiator from generic Markdown editors is the **multi-workspace sidebar** (you can mount several unrelated folders side-by-side) and the **A4-page reading view** that mirrors the export output, so what you write is what you print.

### Single screenshot description

When fully populated, the window has three vertical columns:

```
┌──────────────────────┬────────────────────────────────────────────────────────────┐
│ [search box]         │ ITFORCE MARKDOWN EDITOR    [Open Folder][Open File][New File]  │
├──────────────────────┼────────────────────────────────────────────────────────────┤
│ ▼ Trent MD       💡✕ │ ┌──────────────────────────────────────────────────────┐     │
│   Untitled.md        │ │ 📄 Workflow POC  ✏                                    │     │
│   Workflow POC.md ★  │ │    .../Trent MD/Workflow POC.md  📋                  │     │
│   记事本.md           │ │                                                       │     │
│                      │ │  Mode [Read][Edit][Source]  🗑 📑 ⤢ ⇪ Save  ✕         │     │
│ ▶ bpm           💡✕  │ ├──────────────────────────────────────────────────────┤     │
│                      │ │ OUTLINE       │  [B I S H1 H2 H3 P ≡ ↺...] ● Saved 17:58 │
│                      │ ├───────────────┼──────────────────────────────────────┤     │
│                      │ │ Workflow POC  │                                      │     │
│                      │ │   POC Overview│   # Workflow POC                     │     │
│                      │ │   测试预期目标 │   …                                  │     │
│                      │ │ [Filter…]     │                                      │     │
│                      │ │               │                                      │     │
└──────────────────────┴────────────────────────────────────────────────────────────┘
```

Left column (≈300 px): workspaces sidebar. Top-right (56 px tall): action bar with app title and 3 toolbar buttons. Below it: document header (title, path, mode picker, action icons). Below that: outline column (≈270 px) + editor area. Multiple resizable splitters separate everything.

---

## 2. Recommended Tech Stack

| Need | Technology | Why |
|---|---|---|
| UI framework | **WPF** on **.NET 8** | Mature, MVVM-friendly, similar mental model to SwiftUI (XAML ≈ View, ObservableObject ≈ INotifyPropertyChanged) |
| Markdown preview / edit | **WebView2** (Edge Chromium runtime) | 1:1 functional replacement for `WKWebView`; same CSS / JS works |
| Markdown → HTML | Translate `MarkdownEngine.swift` to C# **as-is** (see §7) | Output must be byte-identical to macOS for Export PDF parity |
| Source editor | **AvalonEdit** (NuGet) | Lightweight, syntax-highlight-capable, supports `Ctrl+F` find bar out-of-box |
| PDF export | `WebView2.CoreWebView2.PrintToPdfAsync` | Identical to macOS `WKWebView.createPDF` |
| Word export | **DocumentFormat.OpenXml** SDK | Pure OOXML, no Process spawn, sandbox-clean for MSIX |
| Print | `WebView2.CoreWebView2.ShowPrintUIAsync` | Native system print dialog |
| File dialogs | `Microsoft.Win32.OpenFileDialog` / `OpenFolderDialog` (.NET 8) | Standard Windows file pickers |
| Persistence | JSON files in `%LOCALAPPDATA%\ITforceMarkdownPro\` | Equivalent to macOS UserDefaults |
| Recycle bin delete | `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(..., RecycleOption.SendToRecycleBin)` | Equivalent to NSFileManager.trashItem |
| Single-instance | `Mutex` + named pipe argument forwarding | Match macOS Window scene singleton behavior |
| Packaging | **MSIX** via Windows Application Packaging Project | Microsoft Store distribution; auto-update; clean uninstall |

### Project structure (recommended)

```
ITforceMarkdownPro/
├── ITforceMarkdownPro.sln
├── src/
│   ├── App/                          ← WPF executable
│   │   ├── App.xaml
│   │   ├── App.xaml.cs
│   │   ├── MainWindow.xaml
│   │   ├── MainWindow.xaml.cs
│   │   ├── Views/
│   │   │   ├── WorkspaceSidebarView.xaml
│   │   │   ├── DocumentHeaderView.xaml
│   │   │   ├── EditorPanelView.xaml
│   │   │   ├── OutlinePanelView.xaml
│   │   │   ├── EditorTopStripView.xaml
│   │   │   ├── TopActionBarView.xaml
│   │   │   ├── EmptyDocumentStateView.xaml
│   │   │   ├── AboutView.xaml
│   │   │   └── ErrorToastView.xaml
│   │   ├── ViewModels/
│   │   │   ├── AppState.cs            ← equivalent to macOS WorkspaceStore
│   │   │   ├── WorkspaceVM.cs
│   │   │   ├── DocumentVM.cs
│   │   │   └── …
│   │   ├── Services/
│   │   │   ├── MarkdownService.cs     ← wraps Engine
│   │   │   ├── WorkspaceService.cs    ← file system scan
│   │   │   ├── RecentsService.cs
│   │   │   ├── ExportService.cs       ← PDF + Word
│   │   │   ├── PrintService.cs
│   │   │   └── PersistenceService.cs
│   │   ├── Engine/
│   │   │   ├── MarkdownEngine.cs      ← 1:1 port of MarkdownEngine.swift
│   │   │   └── Resources/
│   │   │       ├── document.css       ← extracted from documentHTML
│   │   │       ├── document.js        ← extracted from documentHTML
│   │   │       ├── print.css          ← extracted from printHTML
│   │   │       └── ContentEditableBridge.js
│   │   ├── Models/
│   │   │   ├── Workspace.cs
│   │   │   ├── FileNode.cs
│   │   │   ├── HeadingItem.cs
│   │   │   ├── MarkdownDocument.cs
│   │   │   ├── DocumentSearchResult.cs
│   │   │   └── EditorMode.cs
│   │   └── Resources/
│   │       └── icons/...
│   └── App.Tests/                    ← xUnit tests
└── packaging/
    └── ITforceMarkdownPro.Package/   ← MSIX packaging project
        ├── Package.appxmanifest
        └── Images/
```

---

## 3. Scope & Non-Goals (v1)

### In scope

Every feature listed in §9-§14. Especially:

- Multi-workspace sidebar (mount any number of local folders)
- Three editor modes (Read / Edit / Source)
- Rich editor toolbar (Bold, italic, headings, lists, blockquote, code, link, image, table, undo, redo)
- Outline panel with filter
- Cross-workspace document search
- Open Recent (15 entries, persisted)
- Browser-style document back/forward navigation (Ctrl+[, Ctrl+])
- File ops: open, save, rename-from-H1, clone, delete to Recycle Bin
- Export PDF, Export Word, Print
- Unsaved-changes prompts (close window, close document, switch document, quit)
- Title bar dirty marker
- Drag-and-drop .md files into window
- App-launches-with-file (associate .md / .markdown handler)
- Standard File / Edit / View / Window / Help menus with conventional shortcuts

### Out of scope (v1) — DO NOT IMPLEMENT

- **GitLab sync.** The macOS Pro version has a GitLab tab that clones a project and treats the local checkout as a workspace. Windows v1 has only "Local Folder" workspaces. The mode-switcher UI is not present in v1. Reason: would require shelling out to `git` from a sandboxed MSIX app, which is non-trivial; rebuild as a v2 feature with libgit2sharp later.
- iCloud Drive / OneDrive integration beyond what the OS gives for free.
- Multiple windows. Single-window app.
- Themes / dark mode. The macOS app forces light color scheme; the Windows port should also be light-only in v1.
- Localization. English-only in v1 (UI strings).
- In-app updater. MSIX from Store auto-updates.

---

## 4. Architecture

### MVVM with services

- **Models**: pure data records (`Workspace`, `FileNode`, `HeadingItem`, …). No behavior, no INotifyPropertyChanged.
- **Services**: stateless or single-instance components doing IO/computation: `MarkdownService`, `WorkspaceService`, `PersistenceService`, `RecentsService`, `ExportService`, `PrintService`.
- **ViewModels**: observable wrappers around models + service calls. The top-level `AppState` is registered as a singleton in DI and corresponds to the macOS `WorkspaceStore` class.
- **Views**: XAML + minimal code-behind; bind to ViewModels.

### Dependency injection

Use `Microsoft.Extensions.Hosting` with `Microsoft.Extensions.DependencyInjection`:

```csharp
// App.xaml.cs
public partial class App : Application
{
    public static IHost Host { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        Host = Microsoft.Extensions.Hosting.Host
            .CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<AppState>();
                services.AddSingleton<MarkdownService>();
                services.AddSingleton<WorkspaceService>();
                services.AddSingleton<RecentsService>();
                services.AddSingleton<ExportService>();
                services.AddSingleton<PrintService>();
                services.AddSingleton<PersistenceService>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        Host.Start();
        var window = Host.Services.GetRequiredService<MainWindow>();
        window.Show();
        base.OnStartup(e);
    }
}
```

### Threading

WebView2 callbacks and most service operations are async. All UI state mutation must happen on the WPF dispatcher (the `Dispatcher.CurrentDispatcher`). Use `await` in event handlers; do not block UI thread on `.Result`.

### Communication between UI and WebView2 (Edit mode)

Just like the macOS version uses `window.webkit.messageHandlers.editorChanged.postMessage(markdown)` to send the rebuilt Markdown back to Swift after every keystroke, the Windows version uses:

```js
// in document.js (running inside WebView2)
window.chrome.webview.postMessage({ kind: 'editorChanged', markdown: marker });
```

```csharp
// in MainWindow.xaml.cs
webView.WebMessageReceived += (s, e) =>
{
    var payload = JsonSerializer.Deserialize<WebMessage>(e.WebMessageAsJson);
    if (payload?.Kind == "editorChanged")
        AppState.UpdateSourceDraft(payload.Markdown);
};
```

---

## 5. Data Model

All model types are immutable C# `record`s (or `record struct`s where small). Keep them in `Models/`.

```csharp
public enum EditorMode { Read, Edit, Source }

public record Workspace
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Path { get; init; } = "";
    public bool HideEmptyFolders { get; init; } = true;
    public bool IsExpanded { get; init; } = true;

    public string Name => System.IO.Path.GetFileName(Path);
    public Uri Url => new(Path);
}

public record FileNode
{
    public string Path { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsDirectory { get; init; }
    public ImmutableArray<FileNode> Children { get; init; } = ImmutableArray<FileNode>.Empty;
    public int MarkdownCount { get; init; }

    public string Id => Path;
    public string DisplayCount => MarkdownCount == 0 ? "" : MarkdownCount.ToString();
}

public record MarkdownDocument
{
    public string Path { get; init; } = "";

    public string Id => Path;
    public string Filename => System.IO.Path.GetFileName(Path);
    public string Title => System.IO.Path.GetFileNameWithoutExtension(Path);
}

public record HeadingItem
{
    public string Id { get; init; } = "";       // slug, used as HTML id="…"
    public string Title { get; init; } = "";    // plain text, no markdown
    public int Level { get; init; }             // 1..6
    public int Line { get; init; }              // 0-based line number in source
}

public record DocumentSearchResult
{
    public string Path { get; init; } = "";
    public string Title { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public string Snippet { get; init; } = "";
    public int Line { get; init; }              // 1-based for display

    public string Id => $"{Path}#{Line}#{Snippet}";
}

public record RichEditorCommand
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string JavaScript { get; init; } = "";
}
```

### AppState (singleton VM)

Implements `INotifyPropertyChanged`. Tracks every piece of UI state. Key properties:

| Property | Type | Notes |
|---|---|---|
| `Workspaces` | `ObservableCollection<Workspace>` | Persisted; ordered |
| `WorkspaceTrees` | `Dictionary<Guid, ImmutableArray<FileNode>>` | Rebuilt by `WorkspaceService.RefreshAll` |
| `ActiveWorkspaceUrl` | `string?` | The workspace containing `SelectedFile`, or first |
| `SelectedFile` | `MarkdownDocument?` | The currently open document |
| `SelectedFolderUrl` | `string?` | Target folder for new-document |
| `SourceDraft` | `string` | The in-memory markdown text (may differ from disk if `IsDirty`) |
| `Outline` | `ImmutableArray<HeadingItem>` | Parsed from `SourceDraft` |
| `EditorMode` | `EditorMode` | Default `Read` |
| `SearchText` | `string` | Live search input |
| `SearchResults` | `ImmutableArray<DocumentSearchResult>` | Updated on search input change |
| `IsDirty` | `bool` | `SourceDraft` ≠ on-disk content |
| `StatusMessage` | `string` | One-line status shown next to save indicator |
| `LastSavedAt` | `DateTime?` | For "Saved HH:mm:ss" label |
| `ScrollTargetId` | `string?` | Heading id to scroll WebView to |
| `ScrollToken` | `Guid` | Bumped when ScrollTargetId is set, even if same value |
| `SourceScrollLine` | `int?` | Equivalent for source mode |
| `RichReloadToken` | `Guid` | Force-reload trigger for Edit-mode WebView |
| `RichCommand` | `RichEditorCommand?` | JS to dispatch to the Edit WebView |
| `ErrorMessage` | `string?` | If non-null, an ErrorToast is shown bottom-right |
| `IsSidebarHidden` | `bool` | Full-screen reading mode toggle |
| `RecentDocuments` | `ObservableCollection<string>` | Paths, max 15, MRU |
| `BackStack` | `ObservableCollection<string>` | Paths, last 50 |
| `ForwardStack` | `ObservableCollection<string>` | Paths |

Methods:

- `AddWorkspace()` — pick folder via OpenFolderDialog, append, persist, refresh
- `RemoveWorkspace(Guid)` — remove + persist + cleanup
- `ToggleHideEmpty(Guid)`, `ToggleExpanded(Guid)`
- `CreateDocument(in: Guid?)` — write `Untitled.md` with starter content, open it, switch to Edit
- `CreateFolder()` — create `New Folder` in current writable target
- `RefreshTree()`
- `SelectFile(string url) → bool` — see §11 for the dirty-check + history-push semantics
- `OpenExternalFile(string url)` — handle file opened from OS / drag-drop
- `Save()`
- `DeleteCurrentFile()` — to Recycle Bin
- `RenameCurrentFile(string newBaseName)`
- `CloneCurrentDocument()`
- `SendRichCommand(string js)` — set `RichCommand = new RichEditorCommand(JavaScript = js)`
- `Jump(HeadingItem)` — set ScrollTargetId + bump ScrollToken
- `ClearSearch()`, `UpdateDocumentSearch()`
- `GoBack()`, `GoForward()`
- `CloseCurrentDocument()`
- `ConfirmCloseIfDirty() → bool` — shows the Save/Don't Save/Cancel dialog (see §14)

---

## 6. Persistence

Location: `%LOCALAPPDATA%\ITforceMarkdownPro\` (i.e. `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)`).

Three files, JSON, UTF-8, indented:

### workspaces.json

```json
[
  {
    "id": "8E1A7F22-…",
    "path": "C:\\Users\\chungu\\Documents\\Trent MD",
    "hideEmptyFolders": true,
    "isExpanded": true
  },
  {
    "id": "F35C0911-…",
    "path": "C:\\Users\\chungu\\Documents\\Codex\\bpm",
    "hideEmptyFolders": true,
    "isExpanded": false
  }
]
```

Saved on every mutation to `AppState.Workspaces`. Loaded once at startup. Entries whose `path` does not exist on disk are kept (so the sidebar shows them grayed out and a user can hover for help text), but their trees are empty until the path becomes valid again or the user removes them.

### recents.json

```json
[
  "C:\\Users\\chungu\\Documents\\Trent MD\\Workflow POC.md",
  "C:\\Users\\chungu\\Documents\\Trent MD\\记事本.md"
]
```

Plain string array of absolute paths. MRU order (index 0 = most recent). Cap at 15. Pushed by `RecentsService.Push(path)`; the service does the dedupe-and-promote and truncate.

### settings.json

```json
{
  "hideEmptyFoldersDefault": true,
  "lastWindowFrame": { "x": 100, "y": 80, "width": 1400, "height": 900 },
  "isSidebarHidden": false
}
```

Updated on app exit. The window frame is restored on startup if present and the rect lies on a connected monitor; otherwise fall back to default (`1240 × 760`, centered).

---

## 7. Markdown Engine

### Strategy

The macOS app uses a hand-written 540-line Markdown parser in `MarkdownEngine.swift`. Export PDF and Print depend on this exact HTML for layout to be reproducible. **Translate the Swift engine to C# as-is.** Do not substitute Markdig at the top level.

(Markdig may be used internally as a *secondary* renderer for diagnostic purposes, but the default `MarkdownEngine.RenderHtml(markdown)` must produce the same string the Swift version produces.)

### Public API (port of the Swift `enum MarkdownEngine`)

```csharp
public static class MarkdownEngine
{
    public static ImmutableArray<HeadingItem> ParseHeadings(string markdown);
    public static string RenderHtml(string markdown);
    public static string DocumentHtml(string markdown, bool editable);
    public static string PrintHtml(string markdown, string title, PrintTarget target);
    public static string Slug(string title);
    public enum PrintTarget { Pdf, Word }
}
```

### Block parser algorithm (port verbatim — do not optimize, do not "improve")

Iterate `markdown.Split('\n')` (preserve empty lines), tracking:
- `inCode: bool` — currently inside a ``` fenced block
- `codeFenceLanguage: string` — set on opening ```, cleared on closing
- `codeBuffer: List<string>` — accumulates lines inside the fence
- `listKind: string?` — `"ul"` or `"ol"`, used to detect when to emit `</ul>` / `</ol>`
- `slugCounts: Dictionary<string,int>` — disambiguates duplicate headings (`section`, `section-2`, `section-3`, …)

Block recognition rules (in order):

1. **Code fence**: line trimmed starts with ` ``` `. Toggles `inCode`. On open: `codeFenceLanguage = trimmed.Substring(3).Trim()`. On close: emit `<pre><code{class}>…</code></pre>` where `class=" class=\"language-{escapedLang}\""` if language non-empty, else empty.

2. **Inside code fence**: append raw (non-trimmed) line to `codeBuffer`, continue.

3. **Blank line**: close any open list, continue.

4. **Horizontal rule**: trimmed line equals `"---"` or `"***"` → emit `<hr>`.

5. **Table** (see "Table parsing" below): scan ahead for valid table; on match emit `<table>…</table>` and jump index past it.

6. **Heading**: trimmed line starts with 1-6 `#` followed by a space. Emit `<h{n} id="{slug}">{inline}</h{n}>` where slug is computed (see below) with duplicate-disambiguation suffix.

7. **Blockquote**: trimmed line starts with `>`. Drop the `>` and following whitespace, emit `<blockquote>{inlineRender(rest)}</blockquote>`. (Note: blockquotes are single-line only in this engine; multi-line is **not** supported. Don't add it.)

8. **List item**: matches one of two patterns:
   - Unordered: starts with `"- "` or `"* "` (`ordered=false`, text = remainder after the 2 chars)
   - Ordered: matches regex `^\d+\.\s+(.+)$` (`ordered=true`, text = capture group)

   If current `listKind` differs from desired, close previous list and emit `<ul>` or `<ol>`. Then emit `<li>{inlineRender(text)}</li>`.

9. **Default (paragraph)**: emit `<p>{inlineRender(trimmed)}</p>`.

After the main loop, if `inCode` is still true (unclosed fence), emit `<pre><code>{buffer}</code></pre>` anyway. Close any open list.

Join all emitted strings with `"\n"`.

### Table parsing

A table requires at least two lines: a header line and a divider line. Both must contain `|`. The divider line, after stripping all `|` chars, must contain `---`.

```
| col 1 | col 2 |
| ----- | ----- |
| a     | b     |
| c     | d     |
```

The parser greedily consumes subsequent rows that contain `|` until it hits a blank line or non-`|` line. Each row is split by `|`, then leading/trailing `|` removed before splitting, each cell trimmed of whitespace, each cell rendered with `inlineRender`.

Emit:
```html
<table>
  <thead><tr>{header cells as <th>{inline}</th>}</tr></thead>
  <tbody>{body rows as <tr>{cells as <td>{inline}</td>}</tr>}</tbody>
</table>
```

### Inline parser (`inlineRender`)

Run these substitutions on the input, **in this order** (each on the result of the previous):

```
0.  HTML-escape  &  <  >  "    (NOT ')
1.  ![alt](src)    →  <img alt="$1" src="$2">
2.  [text](href)   →  <a href="$2">$1</a>
3.  `code`         →  <code>$1</code>
4.  ~~strike~~     →  <del>$1</del>
5.  **bold**       →  <strong>$1</strong>
6.  __bold__       →  <strong>$1</strong>
7.  *italic*       →  <em>$1</em>
8.  _italic_       →  <em>$1</em>
```

Use regex with greedy matching. Order matters: **strikethrough must run before bold/italic** so the `~~` delimiters aren't eaten by `*` rules.

Regex patterns (verbatim from Swift, all `.regular` not `.dotMatchesLineSeparators`):

```
1. !\[([^\]]*)\]\(([^\)]+)\)
2. \[([^\]]+)\]\(([^\)]+)\)
3. `([^`]+)`
4. ~~([^~]+)~~
5. \*\*([^*]+)\*\*
6. __([^_]+)__
7. \*([^*]+)\*
8. _([^_]+)_
```

### Slug generation (`Slug`)

```
1. Strip markdown inline markers from the title (drop `!`, `[`, `]`, `(`, `)`, `*`, `_`, `~`, `` ` ``) — used to keep the slug clean of formatting noise.
2. Lowercase.
3. For each unicode scalar: keep alphanumerics, replace anything else with '-'.
4. Collapse runs of '-' into single '-' (regex: -+ → -).
5. Trim leading/trailing '-'.
6. If result is empty, return "section".
```

When the same slug is emitted twice in one document, suffix with `-2`, `-3`, …:

```csharp
var seen = new Dictionary<string,int>();
var slug = MarkdownEngine.Slug(title);
var count = seen.GetValueOrDefault(slug, 0);
seen[slug] = count + 1;
var finalId = count == 0 ? slug : $"{slug}-{count + 1}";
```

### HTML escape

```csharp
static string EscapeHtml(string s) =>
    s.Replace("&", "&amp;")
     .Replace("<", "&lt;")
     .Replace(">", "&gt;")
     .Replace("\"", "&quot;");

static string EscapeAttribute(string s) =>
    EscapeHtml(s).Replace("'", "&#39;");
```

### Acceptance test

For a representative corpus of `.md` files (you may use any well-known Markdown test suite), the C# `MarkdownEngine.RenderHtml(md)` must produce a string equal to what `MarkdownEngine.renderHTML(from: md)` produces in Swift, **byte for byte**. This is required because PDF/Word export pipelines feed this HTML into WebView2 — any HTML difference shows up as visual difference in the printed output.

If you cannot achieve byte-for-byte parity due to unicode normalization differences, normalize both with `String.Normalize(NormalizationForm.FormC)` before comparing.

---

## 8. HTML Templates (CSS / JS)

There are two HTML templates. **Copy the CSS and inline JS verbatim** from the macOS app. Extract them to `Engine/Resources/document.css`, `document.js`, `print.css`, then load and concatenate at runtime.

### 8.1 `DocumentHtml(markdown, editable)` — live preview / WYSIWYG

Used by Read mode (editable=false) and Edit mode (editable=true). Produces a full HTML document with embedded CSS and JS. The Markdown is rendered to HTML server-side via `RenderHtml`, then the resulting HTML is escaped for JavaScript template literal interpolation, then assigned via `doc.innerHTML = \`...\`` inside the page.

Skeleton:

```html
<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <style>{document.css contents}</style>
</head>
<body>
  <article id="doc" class="page" contenteditable="{editableFlag}" spellcheck="true"></article>
  <script>
    const doc = document.getElementById('doc');
    doc.innerHTML = `{escapedRenderedHtml}`;
    {document.js contents}
  </script>
</body>
</html>
```

Where `{editableFlag}` is `"true"` or `"false"`, and `escapedRenderedHtml` is the renderHtml output with three escapes applied **in order**:
1. `\` → `\\`
2. `` ` `` → `` \` ``
3. `$` → `\$`

#### document.css (verbatim — embed as resource)

```css
:root {
  color-scheme: light;
  --ink: #1f2937;
  --muted: #6b7280;
  --line: #e6dece;
  --soft: #faf7f1;
  --accent: #f5a623;
  --code: #f6f1e8;
}
* { box-sizing: border-box; }
html, body {
  margin: 0;
  min-height: 100%;
  background: #f7f7f8;
  color: var(--ink);
  font: 12px/1.58 -apple-system, BlinkMacSystemFont, "SF Pro Text", "Segoe UI", sans-serif;
}
body {
  padding: 36px 24px 64px;
  display: flex;
  justify-content: center;
}
/* Reading window fixed at A4 paper width (210mm @ 96dpi = 794px); shrinks
   to 100% when window narrower than that. Wide tables / long URLs handled
   by td/th word-break + table-layout: fixed below. */
.page {
  width: 794px;
  max-width: 100%;
  min-height: calc(100vh - 104px);
  margin: 0 auto;
  padding: 72px 96px;
  background: white;
  border: 1px solid var(--line);
  border-radius: 4px;
  box-shadow: 0 18px 42px rgba(31, 41, 55, 0.08);
}
.page[contenteditable="true"] {
  outline: none;
  caret-color: var(--accent);
}
h1, h2, h3, h4, h5, h6 {
  line-height: 1.25;
  margin: 1.35em 0 0.55em;
  color: #111827;
  scroll-margin-top: 32px;
}
h1 {
  margin-top: 0;
  padding-bottom: 14px;
  border-bottom: 1px solid var(--line);
  font-size: 2rem;
  letter-spacing: 0;
}
h2 { font-size: 1.5rem; }
h3 { font-size: 1.18rem; }
p { margin: 0 0 1rem; }
strong { font-weight: 700; }
em { color: #374151; }
a { color: #b96904; text-decoration-thickness: 1px; }
blockquote {
  margin: 1.25rem 0;
  padding: 1rem 1.25rem;
  border-left: 4px solid var(--accent);
  background: var(--soft);
  border-radius: 0 8px 8px 0;
  color: #4b5563;
}
ul, ol { margin: 0 0 1.15rem 1.4rem; padding: 0; }
li { margin: 0.25rem 0; }
code {
  padding: 0.12rem 0.35rem;
  background: var(--code);
  border: 1px solid var(--line);
  border-radius: 5px;
  font: 0.92em "SF Mono", ui-monospace, Menlo, Consolas, monospace;
}
pre {
  overflow: auto;
  margin: 1.25rem 0;
  padding: 1rem;
  background: #1f2937;
  color: #f9fafb;
  border-radius: 8px;
}
pre code {
  padding: 0;
  border: 0;
  background: transparent;
  color: inherit;
}
table {
  width: 100%;
  border-collapse: collapse;
  margin: 1.25rem 0;
  overflow: hidden;
  border-radius: 8px;
  table-layout: fixed;
}
th, td {
  padding: 0.78rem 1rem;
  border-bottom: 1px solid var(--line);
  text-align: left;
  word-break: break-word;
  overflow-wrap: anywhere;
}
th {
  background: var(--soft);
  color: #6b7280;
  font-size: 0.78rem;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}
img {
  max-width: 100%;
  border-radius: 8px;
}
hr {
  border: 0;
  border-top: 1px solid var(--line);
  margin: 2rem 0;
}
::selection { background: rgba(245, 166, 35, 0.24); }
```

Adjust the font stack to put a Windows-native font earlier, e.g.:
```
font: 12px/1.58 -apple-system, "Segoe UI", "SF Pro Text", BlinkMacSystemFont, sans-serif;
```
keeping `-apple-system` and BlinkMacSystem in the list is harmless (they degrade silently on Windows).

#### document.js (verbatim — embed as resource, then concat after the `doc.innerHTML = ...` line in the template)

```js
function notifyChange() {
  if (doc.contentEditable !== 'true') return;
  const markdown = blocksToMarkdown(doc);
  // Windows: WebView2 IPC instead of WebKit.
  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.postMessage({ kind: 'editorChanged', markdown });
  }
}

let notifyTimer = null;
doc.addEventListener('input', () => {
  clearTimeout(notifyTimer);
  notifyTimer = setTimeout(notifyChange, 160);
});

function runCommand(command, value = null) {
  doc.focus();
  document.execCommand(command, false, value);
  notifyChange();
}

function formatBlock(tagName) {
  doc.focus();
  document.execCommand('formatBlock', false, tagName);
  notifyChange();
}

function insertLink() {
  // Windows: WebView2 blocks window.prompt; the toolbar button on the host side
  // shows a native input dialog and then dispatches runCommand('createLink', url) via
  // ExecuteScriptAsync. So this function is here for completeness but not called.
}

function insertImage() {
  // (same as insertLink)
}

function insertTable() {
  doc.focus();
  document.execCommand('insertHTML', false,
    '<table><thead><tr><th>Column</th><th>Column</th></tr></thead><tbody><tr><td>Value</td><td>Value</td></tr></tbody></table><p><br></p>');
  notifyChange();
}

function scrollToHeading(id) {
  const el = document.getElementById(id);
  if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
}

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
      const match = cls.match(/language-([\w+\-.]+)/);
      if (match) lang = match[1];
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
    if (checkbox) {
      prefix = checkbox.checked ? '[x] ' : '[ ] ';
    }
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
```

### 8.2 `PrintHtml(markdown, title, target)` — for PDF / Word / Print

Used by Export PDF, Export Word, Print. Two flavors:
- `PrintTarget.Pdf`: body has `padding: 96px` (1 inch all sides), font sizes use `pt` units
- `PrintTarget.Word`: body has no padding (Word handles page margins via `@page`), font sizes use `px` units

The reason for `px` in Word: when OpenXml SDK or any HTML→DOCX converter sees CSS `pt`, it treats it as CSS `px` and applies a 1.333× scaling factor, so 10pt CSS becomes 13.33pt in Word. Using `px` units (which map 1:1 to NSFont pt and Word pt) sidesteps this.

Build a parameterized stylesheet by string-substituting `{u}` with either `pt` or `px`:

```csharp
private static string PrintCommonStyle(string u) => $@"
    @page {{ size: A4; margin: 2.54cm; }}
    html {{ margin: 0; padding: 0; background: white; }}
    body {{
      color: #111827;
      font-family: -apple-system, ""Segoe UI"", BlinkMacSystemFont, sans-serif;
      font-size: 10{u};
      line-height: 1.55;
    }}
    h1, h2, h3, h4, h5, h6 {{
      color: #111827;
      page-break-after: avoid;
      margin: 1.0em 0 0.4em;
      line-height: 1.25;
      font-weight: 700;
    }}
    h1 {{ font-size: 14{u}; margin-top: 0; padding-bottom: 4{u}; border-bottom: 1px solid #d1d5db; }}
    h2 {{ font-size: 12{u}; }}
    h3, h4, h5, h6 {{ font-size: 10{u}; }}
    p, li, blockquote {{ font-size: 10{u}; }}
    p {{ margin: 0 0 0.5em; }}
    a {{ color: #1d4ed8; text-decoration: underline; }}
    strong {{ font-weight: 700; }}
    em {{ font-style: italic; }}
    ul, ol {{ margin: 0 0 0.6em 1.4em; padding: 0; }}
    li {{ margin: 0.15em 0; }}
    blockquote {{
      margin: 0.6em 0;
      padding: 0.5em 0.9em;
      border-left: 3px solid #9ca3af;
      background: #f3f4f6;
      color: #374151;
    }}
    code {{
      padding: 1px 4px;
      background: #f3f4f6;
      border: 1px solid #e5e7eb;
      border-radius: 3px;
      font-family: ""Consolas"", ""SF Mono"", Menlo, monospace;
      font-size: 9{u};
    }}
    pre {{
      page-break-inside: avoid;
      margin: 0.6em 0;
      padding: 8{u} 10{u};
      background: #f6f8fa;
      border: 1px solid #e5e7eb;
      border-radius: 4px;
      font-family: ""Consolas"", ""SF Mono"", Menlo, monospace;
      font-size: 9{u};
      line-height: 1.45;
      white-space: pre-wrap;
      word-wrap: break-word;
      color: #1f2937;
    }}
    pre code {{ padding: 0; border: 0; background: transparent; font-size: inherit; }}
    table {{
      width: 100%;
      border-collapse: collapse;
      margin: 0.6em 0;
      page-break-inside: avoid;
      font-size: 10{u};
    }}
    th, td {{
      padding: 5{u} 8{u};
      border: 1px solid #d1d5db;
      text-align: left;
      vertical-align: top;
    }}
    th {{ background: #f3f4f6; font-weight: 700; }}
    img {{ max-width: 100%; height: auto; }}
    hr {{ border: 0; border-top: 1px solid #d1d5db; margin: 1.0em 0; }}
";

public static string PrintHtml(string markdown, string title, PrintTarget target)
{
    var rendered = RenderHtml(markdown);
    var safeTitle = EscapeHtml(string.IsNullOrEmpty(title) ? "Document" : title);
    var (unit, bodyPadding) = target switch
    {
        PrintTarget.Pdf  => ("pt", "body { margin: 0; padding: 96px; }"),
        PrintTarget.Word => ("px", "body { margin: 0; padding: 0; }"),
        _ => throw new ArgumentOutOfRangeException()
    };
    return $@"<!doctype html>
<html>
<head>
  <meta charset=""utf-8"">
  <title>{safeTitle}</title>
  <style>
{PrintCommonStyle(unit)}
    {bodyPadding}
  </style>
</head>
<body>
{rendered}
</body>
</html>";
}
```

---

## 9. UI Specification

The window is `1240 × 760` minimum, default `1400 × 900`. Single root `Grid` with two columns separated by a `GridSplitter`. The left column is the **sidebar**, the right is the **document workspace**.

In **full-screen reading mode** (`AppState.IsSidebarHidden == true`), the entire window becomes just the document workspace (no sidebar, no top action bar).

### 9.1 Top Action Bar

Above the document workspace (not the sidebar), full width of the right column. Height: **56 px**. Background: pure white (`#FFFFFF`).

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ ITFORCE MARKDOWN PRO              ⊕Folder   📄File   ✎New                  │
│                                   Open Fold Open File New File              │
└─────────────────────────────────────────────────────────────────────────────┘
```

- Left: Text "ITFORCE MARKDOWN PRO" (font Segoe UI Bold 13pt, color `#474D54` = `RGB(72, 77, 84)`, letter-spacing 0.5), padding-left 20.
- Right: 3 buttons, each 72×44, layout vertical (icon top, label bottom):
  - **Open Folder** — icon `FolderAdd` (Segoe Fluent Icon), tooltip "Add a workspace folder", action: `AppState.AddWorkspace()`
  - **Open File** — icon `Document`, tooltip "Open a Markdown file", action: open file picker, then `AppState.OpenExternalFile(path)`
  - **New File** — icon `EditNote`, tooltip "New Markdown document", action: `AppState.CreateDocument(in: ActiveWorkspace?.Id)`. Disabled if no workspaces.

Button icon size 16px, label 10pt, both same color `#474D54` (40% alpha when disabled).

### 9.2 Sidebar (left column)

Width: min **200**, default **300**, max **400** px. Background `#FAF8F0` (very light cream — RGB 250, 248, 240).

#### 9.2.1 Search row (top)

Height 50 px (10px vertical padding). Centered search box:

```
┌────────────────────────────────────────┐
│ 🔍 Search for documents                 │
└────────────────────────────────────────┘
```

Border 1px `#E0D6C2`, rounded 7px, white background, 12pt text. Placeholder `"Search for documents"` shown when empty. Live updates `AppState.SearchText`, triggers `AppState.UpdateDocumentSearch()`.

#### 9.2.2 When `IsSearching` (search box non-empty)

Replace the rest of the sidebar content with **Search Results**:

- Heading "Search Results" with a `←` back button that calls `AppState.ClearSearch()`.
- For each `DocumentSearchResult`:
  - Filename (semibold, ink color)
  - Relative path (muted, smaller)
  - Snippet (muted, italic, ≤120 chars)
  - On click → `AppState.SelectFile(result.Path)` then send `ScrollTargetId` to the heading on line N (or just scroll-into-view).

#### 9.2.3 When not searching: Workspace sections

For each `Workspace` in `AppState.Workspaces`, render a `WorkspaceSection`:

```
┌──────────────────────────────────────┐
│ ▼ Trent MD               💡 ✕         │  ← active workspace: cream highlight bg
├──────────────────────────────────────┤
│   📄 Untitled.md                      │
│   📄 Workflow POC.md  (highlighted)   │
│   ▼ 📁 docs           [9]             │
│     📄 README.md                       │
│   …                                   │
└──────────────────────────────────────┘
```

**Header row** (clickable chevron toggles expansion, full-row click could also do it):

| Element | Behavior |
|---|---|
| Chevron `▼`/`▶` | Toggle `IsExpanded` (call `AppState.ToggleExpanded(id)`) |
| Workspace name | Plain text, font Segoe UI Semibold 13pt, color `#1F2433`. Truncate at end with ellipsis. Tooltip = full path. |
| **Spacer** | |
| 💡 Lightbulb icon | If `HideEmptyFolders` true: filled lightbulb, color accent (`#F5A623`). If false: outlined lightbulb, color muted (`#6C75A3`). Toggle action. Tooltip = "Showing only folders with .md (click to show all)" or vice versa. |
| ✕ Xmark | `AppState.RemoveWorkspace(id)`. Tooltip "Remove Workspace". |

Header background:
- **Active workspace** (the one whose path contains `SelectedFile` or, if no selected file, the first workspace): warm cream `#FCEFD3` (RGB 252, 239, 211)
- **Inactive**: pale gray `#F2F0E8` (RGB 242, 240, 232)

Header height 32px, horizontal padding 10. Icons 22×22, font size 11, default color `#6C75A3` (`AppTheme.muted`).

**Tree** (only if `IsExpanded`): each `FileNode` rendered with indent 16px per depth. Use `TreeView` or hand-rolled `ItemsControl`.

- Folder row: chevron + folder icon (`#D16E2E` orange — `AppTheme.folder`) + name (`#3C5473` navy — `AppTheme.folderText`) + count badge if `MarkdownCount > 0`. Click expands/collapses + sets `SelectedFolderUrl`.
- File row: doc icon (orange) + filename. Click → `AppState.SelectFile(path)`.
- Selected file: cream highlight `#FFFFEA` background, ink color (`#1F2433`) for text. Filter: only `.md` and `.markdown` files are shown; folders are filtered out if `HideEmptyFolders` is true and that subtree has no `.md` files.

Empty workspace: show "No Markdown files here." in muted text.

#### 9.2.4 Empty state (no workspaces)

Show:

```
   No workspaces yet.
   Click the Open Folder button above to add one.
```

In sidebar, centered, muted text.

### 9.3 Document Workspace (right column)

When no document is open, show **EmptyDocumentState**. Otherwise, show **DocumentHeader** on top + the editor below.

#### 9.3.1 EmptyDocumentState

Centered card:

```
        ┌─┐
        │A│  (icon: doc.richtext, accent color, 58 pt)
        └─┘
   Markdown Document Workspace
   Select a document from the sidebar, or
   drag a .md file onto this window to open it.

   [ ⊕ Add Another Workspace ]  [ 📄 Open File… ]
```

If `Workspaces.Count == 0`, change to first-launch variant:
- Icon: `folder.badge.plus`
- Title: "Welcome to ITforce Markdown Pro"
- Subtitle: "Add a folder of Markdown documents to get started. You can add as many workspaces as you like — each shows up as its own section in the sidebar."
- Primary button: "Add Workspace Folder" → `AppState.AddWorkspace()`
- Tip below buttons: "Tip: you can also drag a .md file from Finder Explorer into this window."

#### 9.3.2 DocumentHeader

Height auto (≈ 60 px), white background.

```
┌────────────────────────────────────────────────────────────────────────────────┐
│ 📄 Workflow POC  ✎                  [Read │Edit│ Source]  🗑 📑 ⤢ ⇪ Save ✕   │
│   .../Trent MD/Workflow POC.md  📋                                              │
└────────────────────────────────────────────────────────────────────────────────┘
```

Layout left → right:

| Element | Details |
|---|---|
| Document icon | `Doc.text.fill`, 26pt, accent color (`#F5A623`), 32×32 box |
| **VStack** (title + path) | |
| → EditableTitle | "Workflow POC" in 20pt bold + a pencil ✏ icon that toggles inline rename |
| → DocumentPathRow | `.../parent/file.md` in 11pt monospaced, muted color. Only last **30 chars** of full path shown, prefixed with `…` if truncated. Tooltip shows full path. Copy `📋` icon next to it: copies full path to clipboard, briefly turns green checkmark for 1.6 sec. |
| **Spacer** | |
| Mode picker | Segmented control, 200px wide, 3 options Read / Edit / Source, no label (just the 3 segments). Bound to `AppState.EditorMode`. |
| 🗑 Trash | Tint red. Click → confirm dialog "Delete <filename>?" "The file will be moved to your Recycle Bin." → `AppState.DeleteCurrentFile()` |
| 📑 Clone (square.on.square) | `AppState.CloneCurrentDocument()` |
| ⤢ Full screen (alt: ⤡ Restore) | Toggle `AppState.IsSidebarHidden`. Icon swaps between the two. Animated 0.18s. |
| ⇪ Export (square.and.arrow.up) | Menu button with two items: "Export as PDF…" / "Export as Word (.docx)…" |
| **Save** | Button with disk icon. Enabled iff `IsDirty`. Calls `AppState.Save()`. |
| ✕ Close | `AppState.CloseCurrentDocument()` |

All non-text buttons are 28×28 white squares with thin border `#E0D6C2`, rounded 6px.

#### 9.3.3 Editor area split

Below DocumentHeader: a horizontal splitter dividing **OutlinePanel** (left) and **EditorPanel** (right).

OutlinePanel default 270px (min 180, max 360). EditorPanel takes the rest with `layoutPriority` so window resize gives extra width to editor.

#### 9.3.4 OutlinePanel

```
┌──────────────────────┐
│ OUTLINE              │  ← 12pt bold, letter-spacing 1.8, muted, padded 22 top 10 bot
│ ┌──────────────────┐ │
│ │ 🔍 Filter        │ │  ← shown only if outline.Count ≥ 6
│ └──────────────────┘ │
│                      │
│ Workflow POC         │  ← H1 = 12pt semibold, ink color
│   POC Overview       │  ← H2 = 12pt semibold, muted; indented 16px per level
│   测试预期目标        │
│                      │
└──────────────────────┘
```

Background `#F7F7FA` (very light blue-gray). Filter box (only when 6+ headings) updates a local `FilterText` and shows only headings whose `Title.ToLowerInvariant()` contains the lowered filter. When file changes, clear the filter.

Click on a heading → `AppState.Jump(heading)` which sets `ScrollTargetId = heading.Id` + bumps `ScrollToken`. The active editor (whichever mode) reacts.

When no headings: "No headings in this document." in muted text.

#### 9.3.5 EditorPanel

VStack:
- **EditorTopStrip** (height 48)
- Divider (1px)
- The actual editor (one of three modes — see §10)

##### EditorTopStrip

```
┌──────────────────────────────────────────────────────────────────────┐
│ [B I S  H1 H2 H3 P  ≡ #️ ❝ </>  🔗 🖼 ⊞  ↺ ↻]    ● Saved 17:58:50    │
└──────────────────────────────────────────────────────────────────────┘
```

- Left: in Edit mode only, the rich editor toolbar (horizontally scrollable).
- Right: SaveStatus always visible.

SaveStatus = small horizontal stack:
- Dot 9×9, green `#4ACC8B` if `!IsDirty`, orange `#F5A623` if `IsDirty`
- "Saved" or "Unsaved" (12pt, muted)
- If `LastSavedAt` non-null: `HH:mm:ss` monospaced 12pt, muted

##### Rich Editor Toolbar (Edit mode)

Horizontal toolbar with these buttons (in this order, separated by 24px dividers):

| Group | Buttons |
|---|---|
| Inline | B (Bold), I (Italic), S (Strikethrough), 🖌 (Highlight = backColor #fff4dc) |
| — | |
| Block | H1, H2, H3, P (paragraph) |
| — | |
| List | • (bullet list), 1. (ordered list), ❞ (quote), `</>` (code block) |
| — | |
| Insert | 🔗 (Link — opens native input dialog for URL), 🖼 (Image), ⊞ (Table) |
| — | |
| History | ↺ Undo, ↻ Redo |

Each button is 24×24 with 12pt semibold icon, color `#1F2433`, plain hover.

Action wiring:
- Bold/Italic/Strikethrough/Highlight/Lists/Quote/Code: send a JS command via `webView.ExecuteScriptAsync($"runCommand('{cmd}', {valueJson})")`
- H1/H2/H3/P: `formatBlock('h1')` / `'h2'` / `'h3'` / `'p'`
- Link/Image: Windows can't use JS `prompt` reliably from `ExecuteScriptAsync`. **Show a native WPF input dialog** to ask for URL, then send `runCommand('createLink', '<escaped url>')` or `runCommand('insertImage', '<escaped url>')`.
- Table: `insertTable()` (already in document.js).
- Undo/Redo: `runCommand('undo')` / `runCommand('redo')`.

---

## 10. Editor Modes

### 10.1 Read mode

WebView2 hosting `DocumentHtml(SourceDraft, editable: false)`. Reload triggered when `RenderedHtmlReloadKey` changes; the key is `"{SelectedFile?.Id ?? "none"}|preview|{SourceDraft.GetHashCode()}"`.

On scroll-to-heading: when `ScrollTargetId` changes (token check), call `webView.ExecuteScriptAsync($"scrollToHeading('{escapedId}')")` after a 120ms delay.

### 10.2 Edit mode (WYSIWYG via contenteditable)

WebView2 hosting `DocumentHtml(SourceDraft, editable: true)`. The reload key is different: `"{SelectedFile?.Id}|rich|{RichReloadToken}"` — reload only happens when token changes, not on every keystroke. Reason: keystrokes update `SourceDraft` via the `editorChanged` IPC message; reloading the page on every keystroke would lose cursor position.

Bump the `RichReloadToken` only when:
- The selected file changes
- The user explicitly switches into Edit mode (from Read or Source)
- The user clicks Save (so the displayed HTML reflects the freshly saved state)

#### IPC

In `MainWindow.xaml.cs`:
```csharp
private async void EditorWebView_CoreWebView2InitializationCompleted(...)
{
    EditorWebView.CoreWebView2.WebMessageReceived += OnWebMessage;
}

private void OnWebMessage(object? s, CoreWebView2WebMessageReceivedEventArgs e)
{
    var msg = JsonSerializer.Deserialize<JsonElement>(e.WebMessageAsJson);
    if (msg.GetProperty("kind").GetString() == "editorChanged")
    {
        var markdown = msg.GetProperty("markdown").GetString() ?? "";
        AppState.UpdateSourceDraft(markdown);
    }
}
```

When `AppState.RichCommand` changes, push the JS:
```csharp
appState.PropertyChanged += async (s, e) =>
{
    if (e.PropertyName == nameof(AppState.RichCommand) && appState.RichCommand != null)
        await EditorWebView.CoreWebView2.ExecuteScriptAsync(appState.RichCommand.JavaScript);
};
```

### 10.3 Source mode

Use **AvalonEdit** (NuGet `AvalonEdit`) — supports Markdown syntax highlight (community-contributed XSHD file available), built-in Find Bar (`Ctrl+F`), reasonable line-numbering opt-in, and is the de-facto standard for embedded code editors in WPF.

Layout: center the text editor in an A4-width (794 px) white card on a gray (`#F5F5F8`) background, with shadow and 24px vertical padding. Just like the WebView versions visually.

Binding: two-way bind text to `AppState.SourceDraft`. On `TextChanged`, call `AppState.UpdateSourceDraft(editor.Text)`. On `SourceScrollLine` change (and token), scroll the editor to that line.

---

## 11. File Operations

### 11.1 Open file (Ctrl+O)

Show `Microsoft.Win32.OpenFileDialog`, filter `*.md;*.markdown`, on OK call `AppState.OpenExternalFile(path)`. `OpenExternalFile` checks:
- If file is in any existing workspace's subtree → just `SelectFile(path)`
- Otherwise, add the containing folder as a new workspace (auto-mount), then `SelectFile(path)`

### 11.2 Open folder (Ctrl+Shift+O)

`AddWorkspace()` — show `OpenFolderDialog` (built into .NET 8), append a new `Workspace` to `AppState.Workspaces`, scan the tree, persist, refresh sidebar.

### 11.3 Open Recent (File menu submenu)

Iterate `AppState.RecentDocuments`. Each entry is a menu item showing just the filename (not full path); click → `OpenExternalFile(fullPath)`. "Clear Menu" item at the bottom calls `RecentsService.Clear()`.

### 11.4 Save (Ctrl+S)

Write `SourceDraft` to `SelectedFile.Path` as UTF-8 (no BOM). On success:
- `IsDirty = false`
- `LastSavedAt = DateTime.Now`
- `StatusMessage = "Saved at HH:mm:ss"`
- Update window title (drop dirty marker)
- **Auto-rename**: if the first `# Heading` in the new content differs from what it was last time the file was opened/saved AND differs from the current filename's basename, **rename the file** to `{sanitized H1}.md`. Use the same sanitization as macOS: replace `/\\:*?"<>|\0` with `_`, collapse `_+` to single `_`, trim leading/trailing space/dot/underscore, cap at 120 chars.

If save fails: set `ErrorMessage`, leave `IsDirty` true.

### 11.5 Close Document (Ctrl+Shift+W or toolbar ✕)

If `IsDirty`: show **Save / Don't Save / Cancel** dialog (see §14). Otherwise: clear the document state (`SelectedFile = null`, `SourceDraft = ""`, etc.).

After close, also reset `IsSidebarHidden` to false (auto-exit full-screen reading mode).

### 11.6 Delete current file (toolbar 🗑 with confirm)

Confirm dialog "Delete <filename>?" message "The file will be moved to your Recycle Bin. You can restore it from File Explorer until the Recycle Bin is emptied." → on confirm, use:

```csharp
Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
    path,
    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin
);
```

(Add NuGet `Microsoft.VisualBasic` if not present.) Then clear document state, refresh tree.

### 11.7 Rename current file (pencil ✏ icon inline rename)

Inline-edit the title field. On commit (Enter or focus loss), sanitize, rename file on disk, update `SelectedFile.Path`, refresh tree.

### 11.8 Clone current document

Find a free filename: `{base}_{n}.md` starting at n=2 (strip existing `_<n>` suffix first so `foo_2.md` clones to `foo_3.md`, not `foo_2_2.md`). Write the in-memory `SourceDraft` to it. Refresh tree, select the clone.

### 11.9 Drag and drop

`MainWindow` listens to `AllowDrop=true` and handles `Drop` event:

```csharp
private void OnDrop(object sender, DragEventArgs e)
{
    if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
    var files = (string[])e.Data.GetData(DataFormats.FileDrop);
    foreach (var f in files)
    {
        var ext = Path.GetExtension(f).ToLowerInvariant();
        if (ext == ".md" || ext == ".markdown")
        {
            await AppState.OpenExternalFile(f);
            break; // open the first md file
        }
    }
}
```

### 11.10 Open from OS (file association)

In MSIX manifest, register as handler for `.md` and `.markdown`. When the app is launched with `args[0]` being a `.md` path, call `OpenExternalFile(args[0])` after the main window is ready. Use the single-instance pattern to forward args when an instance is already running.

---

## 12. Export & Print

### 12.1 Export PDF

```csharp
public async Task ExportPdf(string markdown, string title, string? suggestedPath)
{
    // 1. Show Save dialog
    var dlg = new Microsoft.Win32.SaveFileDialog
    {
        Filter = "PDF (*.pdf)|*.pdf",
        FileName = SuggestedFilename(title, suggestedPath, ".pdf"),
        InitialDirectory = Path.GetDirectoryName(suggestedPath ?? "")
    };
    if (dlg.ShowDialog() != true) return;

    // 2. Render the print HTML
    var html = MarkdownEngine.PrintHtml(markdown, title, PrintTarget.Pdf);

    // 3. Use a hidden WebView2 to render → PDF
    var webView = new WebView2();
    await webView.EnsureCoreWebView2Async();
    webView.Width = 794; webView.Height = 1123;       // A4 @ 96dpi
    var tcs = new TaskCompletionSource<bool>();
    webView.CoreWebView2.NavigationCompleted += async (s, e) =>
    {
        await Task.Delay(150); // let layout settle
        var settings = webView.CoreWebView2.Environment.CreatePrintSettings();
        settings.PageWidth = 8.27;   // inches
        settings.PageHeight = 11.69;
        settings.MarginTop = 0;
        settings.MarginBottom = 0;
        settings.MarginLeft = 0;
        settings.MarginRight = 0;
        settings.ShouldPrintBackgrounds = true;
        await webView.CoreWebView2.PrintToPdfAsync(dlg.FileName, settings);
        tcs.SetResult(true);
    };
    webView.CoreWebView2.NavigateToString(html);
    await tcs.Task;

    // 4. Reveal in File Explorer
    Process.Start("explorer.exe", $"/select,\"{dlg.FileName}\"");
}
```

Margins are 0 in print settings because the HTML body already has 96px padding (= 1 inch).

### 12.2 Export Word (.docx)

Use **DocumentFormat.OpenXml** (NuGet). Convert the in-memory Markdown to OOXML:

**Strategy**: Don't translate the HTML to docx (lossy). Walk the Markdown AST (produced by `MarkdownEngine.RenderHtml`-style logic but adapted to emit a paragraph/run model) and build a `WordprocessingDocument` directly.

For simplicity and good quality, this is the recommended approach:

1. Parse Markdown blocks using the same block parser (§7) but emit a list of "WordBlock" instructions:
   - Heading(level, text)
   - Paragraph(runs) where Run is bold/italic/code/text/link
   - BulletList / NumberedList of items
   - CodeBlock(language, text)
   - BlockQuote(text)
   - Table(rows × cells)
   - HorizontalRule

2. For each WordBlock, append corresponding OOXML elements (`Paragraph`, `Run`, `RunProperties` with `Bold`/`Italic`, `NumberingProperties` for lists, `Table` elements for tables).

3. Apply font sizes matching the spec (in half-points: 10pt = `Size="20"`, 12pt = `Size="24"`, 14pt = `Size="28"`).

4. Save to the user-chosen path.

If you cannot do a clean OpenXml port in time, an acceptable fallback: render `PrintHtml(markdown, title, PrintTarget.Word)` to a temp HTML file, then use a third-party library (e.g. `HtmlToOpenXml.dll`) to convert. The result will be lower-quality than a direct OOXML build but functional.

### 12.3 Print (Ctrl+P)

Render `PrintHtml(markdown, title, PrintTarget.Pdf)` into a hidden WebView2 (same as Export PDF), then on `NavigationCompleted` call:

```csharp
await webView.CoreWebView2.ShowPrintUIAsync(CoreWebView2PrintDialogKind.System);
```

This brings up the native Windows print dialog with a preview, page count, printer selection, paper size, and "Save as PDF" option. The user controls everything from there.

---

## 13. Menus & Keyboard Shortcuts

Standard Windows menu bar. WPF: add a `<Menu>` at the top of `MainWindow`. .NET 8.

| Menu | Item | Shortcut | Action |
|---|---|---|---|
| **File** | New Markdown Document | `Ctrl+N` | `AppState.CreateDocument(null)` |
|  | New Folder | `Ctrl+Shift+N` | `AppState.CreateFolder()` |
|  | — | | |
|  | Open File… | `Ctrl+O` | OpenFileDialog → `OpenExternalFile` |
|  | Open Folder… | `Ctrl+Shift+O` | `AppState.AddWorkspace()` |
|  | Open Recent ▸ | | submenu, see §11.3 |
|  | — | | |
|  | Save | `Ctrl+S` | `AppState.Save()` (disabled if not dirty) |
|  | Close Document | `Ctrl+W` | `AppState.CloseCurrentDocument()` (disabled if no doc open) |
|  | — | | |
|  | Print… | `Ctrl+P` | `PrintService.Print(SourceDraft, Title)` |
|  | — | | |
|  | Exit | `Alt+F4` | `Application.Current.Shutdown()` |
| **Edit** | Undo | `Ctrl+Z` | route to focused control (WebView2 or AvalonEdit handles internally) |
|  | Redo | `Ctrl+Y` | (same) |
|  | — | | |
|  | Cut | `Ctrl+X` | |
|  | Copy | `Ctrl+C` | |
|  | Paste | `Ctrl+V` | |
|  | Select All | `Ctrl+A` | |
|  | — | | |
|  | Find | `Ctrl+F` | AvalonEdit show find bar; for WebView2 modes use the built-in `webView.Find` |
| **View** | Toggle Sidebar | `Ctrl+\` | flip `AppState.IsSidebarHidden` |
|  | — | | |
|  | Back | `Ctrl+[` | `AppState.GoBack()` (disabled if `BackStack` empty) |
|  | Forward | `Ctrl+]` | `AppState.GoForward()` (disabled if `ForwardStack` empty) |
| **Window** | Minimize | `Ctrl+M` | (standard) |
|  | Maximize | (none) | (standard) |
| **Help** | ITforce Markdown Pro Help | | open https://www.fieldsone.com in default browser |
|  | Send Feedback… | | open mailto:info@itforce.ae |
|  | — | | |
|  | About ITforce Markdown Pro | | show AboutWindow |

Notes:
- Use `Ctrl+W` for Close Document (not Close Window — Windows has no equivalent of macOS's window/document separation; ⌘W on Mac is Window close, ⌃⌘W is Document close; on Windows, conflate them — `Ctrl+W` closes the document, and the user closes the window via the title bar `×`).
- `Ctrl+\` for Toggle Sidebar is the convention from VS Code / GitHub / many Windows IDEs.

---

## 14. Window & App Behavior

### 14.1 Title bar dirty marker

Window title: `"{filename}{dirty?}"` where `dirty?` is `" — Edited"` if `IsDirty`, empty otherwise. If no file open, title is `"ITforce Markdown Pro"`.

Set via `MainWindow.Title = $"…";` whenever `SelectedFile` or `IsDirty` changes.

### 14.2 Unsaved changes prompts

Three contexts where this dialog must appear:
- Close Document (Ctrl+W or ✕ button)
- Switch to a different file (sidebar click, Open Recent, drag-drop, ⌘[/])
- Close window / Quit (window close X, Alt+F4)

Dialog:

```
┌──────────────────────────────────────────────────────────┐
│ Do you want to save the changes you made to "foo.md"?    │
│                                                          │
│ Your changes will be lost if you don't save them.        │
│                                                          │
│              [ Don't Save ]  [ Cancel ]  [ Save ]        │
└──────────────────────────────────────────────────────────┘
```

Use `MessageBox.Show(...)` or a custom WPF dialog. Return values:
- Save → call `AppState.Save()`. If save succeeds (`IsDirty == false`), proceed with the close/switch. If save fails (errorMessage set, IsDirty still true), CANCEL the close.
- Don't Save → proceed with close/switch, discarding changes.
- Cancel → abort the close/switch; user stays on current dirty document.

Implementation detail: `AppState.ConfirmCloseIfDirty() → bool` returns true if the caller may proceed (saved or discarded), false if they must abort.

For `SelectFile(path)` callers (sidebar clicks, etc.), `SelectFile` does the check internally and returns bool — the caller is whatever sidebar item's click handler.

For window close, hook `MainWindow.Closing`:
```csharp
protected override void OnClosing(CancelEventArgs e)
{
    if (!AppState.ConfirmCloseIfDirty())
        e.Cancel = true;
    base.OnClosing(e);
}
```

### 14.3 Browser-style document history

- `BackStack` and `ForwardStack` are stacks of file paths.
- On a successful `SelectFile(newPath)` (where new ≠ current AND NOT navigating via Back/Forward), push current path to `BackStack` and clear `ForwardStack`.
- Cap `BackStack` at 50 entries.
- `GoBack()`: peek `BackStack.Last`. If `SelectFile(target)` returns true, then pop `BackStack` and push the previous current to `ForwardStack`.
- `GoForward()`: symmetric.

Use an `IsNavigatingHistory` flag inside `AppState` to skip the auto-push during Back/Forward.

### 14.4 Single instance

Use a named `Mutex` plus an IPC pipe to forward any file path argument to the running instance:

```csharp
// In App.OnStartup before window creation
var mutex = new Mutex(true, "ITforceMarkdownProSingleInstanceMutex", out bool isFirst);
if (!isFirst)
{
    // Forward args via named pipe to running instance
    using var client = new NamedPipeClientStream(".", "ITforceMarkdownProPipe", PipeDirection.Out);
    client.Connect(2000);
    using var writer = new StreamWriter(client);
    writer.WriteLine(string.Join("\n", e.Args));
    Application.Current.Shutdown();
    return;
}
// Main instance: start the pipe server
Task.Run(async () => await RunPipeServerAsync());
```

When the pipe server receives a file path, it dispatches to the UI thread and calls `OpenExternalFile`.

### 14.5 Full-screen reading mode

Toggled by `IsSidebarHidden` (poorly named for historical reasons — it actually hides both sidebar AND top action bar). When true, render only the document workspace, full window.

DocumentHeader still includes the ⤡ Restore button (icon flips). Also, View menu's "Toggle Sidebar" works.

**Auto-exit**: when `SelectedFile` becomes null while in full-screen mode, automatically set `IsSidebarHidden = false` so user isn't trapped.

---

## 15. Visual Design Tokens

Match the macOS app's "warm cream + orange accent" palette exactly. RGB values are 0-255.

| Token | RGB | Hex | Notes |
|---|---|---|---|
| accent | 245, 156, 20 | `#F59C14` | Primary brand orange |
| ink | 28, 35, 51 | `#1C2333` | Primary text |
| folder | 209, 110, 46 | `#D16E2E` | Folder icon color |
| folderText | 60, 84, 115 | `#3C5473` | Folder name in tree |
| muted | 107, 117, 140 | `#6B758C` | Secondary text |
| placeholder | 140, 140, 140 | `#8C8C8C` | Search placeholder |
| line | 224, 214, 194 | `#E0D6C2` | Borders, dividers in sidebar |
| sidebarBackground | 250, 248, 240 | `#FAF8F0` | Sidebar background |
| sidebarSectionActive | 252, 239, 211 | `#FCEFD3` | Active workspace header |
| sidebarSectionInactive | 242, 240, 232 | `#F2F0E8` | Inactive workspace header |
| toolbarIcon | 72, 77, 84 | `#484D54` | Top action bar buttons + app title |
| outlineBackground | 247, 247, 250 | `#F7F7FA` | Outline column background |
| editorBackground | 245, 245, 248 | `#F5F5F8` | Document workspace gray bg |
| selectedBackground | 255, 240, 209 | `#FFF0D1` | Selected file row in tree |
| windowBackground | 245, 245, 248 | `#F5F5F8` | Window default bg |
| topbarAccent | 255, 122, 15 | `#FF7A0F` | Vestigial — for logo, not used in v1 |

Define in WPF as `Application.Resources`:

```xml
<Application.Resources>
    <SolidColorBrush x:Key="AccentBrush" Color="#F59C14"/>
    <SolidColorBrush x:Key="InkBrush" Color="#1C2333"/>
    <SolidColorBrush x:Key="MutedBrush" Color="#6B758C"/>
    <!-- … etc … -->
</Application.Resources>
```

### Fonts

- UI base: **Segoe UI** (Windows native), fall back to `system-ui, sans-serif`.
- Monospaced (code blocks, paths): **Consolas**, fall back to `Cascadia Code, "SF Mono", monospace`.

### Layout sizes

| Element | Size |
|---|---|
| Window min | 1240 × 760 |
| Window default | 1400 × 900 |
| Sidebar min / default / max | 200 / 300 / 400 |
| Outline min / default / max | 180 / 270 / 360 |
| Editor min | 400 (priority 1 → grows with window) |
| TopActionBar height | 56 |
| EditorTopStrip height | 48 |
| DocumentHeader height | auto (~60) |

---

## 16. App Lifecycle

### 16.1 Startup

1. Single-instance check (§14.4)
2. DI container setup
3. `PersistenceService` loads `workspaces.json`, `recents.json`, `settings.json` into `AppState`
4. For each workspace, `WorkspaceService.ScanTree` runs (synchronous; this is fast for typical sizes — defer to background only if it takes >100ms in your tests)
5. If startup args contains a file path, route it through `OpenExternalFile` once the window is shown
6. Restore window position/size from `settings.lastWindowFrame` if valid (rect intersects a visible screen)
7. Show `MainWindow`

### 16.2 During use

- Refresh trees when window receives `Activated` event (catches external file changes)
- Persist `AppState.Workspaces`, `RecentDocuments` on every mutation (debounce 500ms)
- Persist `settings.lastWindowFrame` on `LocationChanged` / `SizeChanged` (debounce 500ms)

### 16.3 Shutdown

- Hook `MainWindow.Closing`: dirty check (§14.2)
- Save settings.json one last time
- Dispose WebView2 cleanly (call `webView.Dispose()`)

---

## 17. Packaging (MSIX)

### 17.1 Project setup

In the .sln, add a new "Windows Application Packaging Project" (`packaging/ITforceMarkdownPro.Package/`). Reference the WPF App project.

### 17.2 Package.appxmanifest

Required entries:

```xml
<Identity Name="com.itforce.MarkdownPro"
          Publisher="CN=ITFORCE TECHNOLOGY DMCC"
          Version="1.1.0.0" />
<Properties>
  <DisplayName>ITforce Markdown Pro</DisplayName>
  <PublisherDisplayName>ITFORCE TECHNOLOGY DMCC</PublisherDisplayName>
  <Description>Markdown editor with multi-workspace support, A4 reading view, and PDF/Word export.</Description>
  <Logo>Images\StoreLogo.png</Logo>
</Properties>

<Dependencies>
  <TargetDeviceFamily Name="Windows.Desktop"
                      MinVersion="10.0.19041.0"
                      MaxVersionTested="10.0.22621.0" />
</Dependencies>

<Capabilities>
  <Capability Name="internetClient" />          <!-- for WebView2 IPC + Help URL -->
  <rescap:Capability Name="runFullTrust" />     <!-- WPF requires full trust -->
</Capabilities>

<Applications>
  <Application Id="App" Executable="App.exe" EntryPoint="$targetentrypoint$">
    <uap:VisualElements DisplayName="ITforce Markdown Pro"
                        Square150x150Logo="Images\Square150x150Logo.png"
                        Square44x44Logo="Images\Square44x44Logo.png"
                        Description="ITforce Markdown Pro"
                        BackgroundColor="transparent">
      <uap:DefaultTile Wide310x150Logo="Images\Wide310x150Logo.png" />
    </uap:VisualElements>

    <Extensions>
      <!-- Register as handler for .md / .markdown -->
      <uap:Extension Category="windows.fileTypeAssociation">
        <uap:FileTypeAssociation Name="markdown">
          <uap:DisplayName>Markdown Document</uap:DisplayName>
          <uap:Logo>Images\Square44x44Logo.png</uap:Logo>
          <uap:SupportedFileTypes>
            <uap:FileType ContentType="text/markdown">.md</uap:FileType>
            <uap:FileType ContentType="text/markdown">.markdown</uap:FileType>
          </uap:SupportedFileTypes>
        </uap:FileTypeAssociation>
      </uap:Extension>
    </Extensions>
  </Application>
</Applications>
```

### 17.3 WebView2 Runtime

Windows 11 ships with WebView2 Evergreen. For Windows 10, ensure the WebView2 Bootstrapper runs on first launch. With MSIX, you can declare `WebView2Loader.dll` dependency or include the Bootstrapper as a setup step. .NET 8 + WebView2 NuGet handles most of this automatically.

### 17.4 Store submission

- Sign with EV cert (Microsoft Store will re-sign as part of cert process)
- Provide screenshots for Store listing (recommended: 1366×768 and 1920×1080)
- Privacy policy URL (since `internetClient` is declared) — link to https://www.itforce.ae/privacy or similar

---

## 18. Acceptance Criteria

Before considering v1 done, verify each of these manually:

### Core flow

- [ ] Launch app first time → see EmptyDocumentState with "Welcome" + "Add Workspace Folder" button
- [ ] Click "Add Workspace Folder" → folder picker → choose a folder containing .md files → sidebar shows workspace section with file tree
- [ ] Click a .md file → opens in Read mode by default with rendered HTML
- [ ] Switch to Edit mode → can edit WYSIWYG, edits update SourceDraft (verify by switching to Source mode)
- [ ] Switch to Source mode → see raw Markdown text, can edit
- [ ] All three modes round-trip: edit in Edit, switch to Source, source matches; edit in Source, switch to Read, render matches

### Persistence

- [ ] Add 2 workspaces, restart app → both still there
- [ ] Open a few docs, restart → File > Open Recent lists them in MRU order
- [ ] Resize window, restart → window restored to that size

### File ops

- [ ] Ctrl+N → new Untitled.md in active workspace, opens in Edit mode
- [ ] Edit & Ctrl+S → file written, dirty marker clears, "Saved HH:mm:ss" updates
- [ ] Edit, don't save, click another file in sidebar → dialog Save/Don't Save/Cancel; Cancel keeps you on current
- [ ] Drag a .md from Explorer into window → opens
- [ ] Double-click a .md in Explorer → app opens with that file (file association)
- [ ] Rename via H1: change first H1, save → file renamed on disk

### Export & Print

- [ ] Export PDF → file opens in default PDF viewer, content matches Read mode, A4 with 1-inch margins
- [ ] Export Word → opens in Word, headings are H1/H2/etc Word styles, font sizes 14pt/12pt/10pt, tables present
- [ ] Print (Ctrl+P) → native print dialog, "Save as PDF" works

### Menus / shortcuts

- [ ] Ctrl+\\ toggles sidebar
- [ ] Ctrl+[ / Ctrl+] navigate document history after opening 3 different files
- [ ] All File menu items work as specified

### Window behavior

- [ ] Close window with unsaved changes → Save/Don't Save/Cancel prompt
- [ ] Title bar shows " — Edited" when dirty
- [ ] Close X in toolbar closes document; if dirty, prompts

### Multi-workspace

- [ ] Add 2 workspaces with overlapping subtree names → both shown distinctly
- [ ] Collapse workspace, restart → still collapsed
- [ ] Lightbulb toggle hides/shows empty folders in that workspace independently

### Editor toolbar (Edit mode)

- [ ] Bold/Italic/Strikethrough/Headings/Lists/Quote/Code/Table all work
- [ ] Link → native input dialog → enter URL → text becomes link
- [ ] Image → input dialog → enter URL → image appears
- [ ] Undo/Redo work

---

## 19. Known Limitations & Risks

### Tested risks

- **WebView2 vs WebKit CSS differences**: 99% identical. Watch out for `-webkit-` prefixed properties (we don't use any in §8.1). `word-break: break-word` and `overflow-wrap: anywhere` work in Edge Chromium 88+. Verify printing renders identically by exporting the same .md to PDF on macOS and Windows and diffing visually.

- **AvalonEdit Markdown syntax**: not as polished as VS Code's. Acceptable for v1.

- **Font rendering**: Segoe UI on Windows looks different from SF Pro on macOS (more rigid). This is expected and OK — users expect Windows apps to look "Windows-y".

- **`document.execCommand`**: deprecated by W3C but still works in Edge Chromium and will for years. We use it the same way the macOS app does for Edit mode toolbar actions.

- **Drag-drop on full-trust MSIX**: works. On AppContainer (more locked-down) it may not — but full-trust MSIX is allowed for desktop apps.

### Open questions for v2

- GitLab sync (port from macOS using libgit2sharp or similar)
- Theme support / dark mode
- Multi-window editing
- Cloud sync via OneDrive
- Spell-check beyond what `contenteditable` + Edge provides

---

## Appendix A: Mapping macOS → Windows for the implementer

| macOS API | Windows equivalent |
|---|---|
| `NSWindow` | WPF `Window` |
| `NSOpenPanel` (folder) | `OpenFolderDialog` (.NET 8) |
| `NSOpenPanel` (file) | `Microsoft.Win32.OpenFileDialog` |
| `NSSavePanel` | `Microsoft.Win32.SaveFileDialog` |
| `NSAlert` runModal | `MessageBox.Show` or custom `Window.ShowDialog()` |
| `NSWindowDelegate.windowShouldClose` | `Window.Closing` event with `e.Cancel = true` |
| `NSWindow.isDocumentEdited` | manually update `Window.Title` string |
| `NSFileManager.trashItem` | `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(..., RecycleOption.SendToRecycleBin)` |
| `NSWorkspace.activateFileViewerSelecting` | `Process.Start("explorer.exe", "/select,\"" + path + "\"")` |
| `NSWorkspace.open(url)` | `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })` |
| `WKWebView` | `Microsoft.Web.WebView2.Wpf.WebView2` |
| `webView.loadHTMLString(html, baseURL: nil)` | `webView.CoreWebView2.NavigateToString(html)` |
| `webView.evaluateJavaScript(script)` | `webView.CoreWebView2.ExecuteScriptAsync(script)` |
| `WKScriptMessageHandler` / `webkit.messageHandlers.X.postMessage` | `WebMessageReceived` event + `window.chrome.webview.postMessage` |
| `NSPrintOperation` + `PDFKit` | `WebView2.CoreWebView2.ShowPrintUIAsync` |
| `WKWebView.createPDF` | `WebView2.CoreWebView2.PrintToPdfAsync` |
| `UserDefaults` | `appsettings.json` or `JsonSerializer` to `%LOCALAPPDATA%` files |
| `NSDocumentController.noteNewRecentDocumentURL` | maintain own list in `RecentsService` (Windows recently-used list is `JumpList`) |
| `JumpList` (bonus) | `System.Windows.Shell.JumpList` — adds recent docs to taskbar right-click menu |
| `NSAttributedString HTML→docFormat` | DocumentFormat.OpenXml direct OOXML build |
| `Process` (spawn /usr/bin/textutil) | n/a; we go OpenXml direct |
| Sandbox entitlements | n/a in v1 (full trust); MSIX `runFullTrust` capability declared |
| `@MainActor` | WPF `Dispatcher` (single UI thread); use `async/await` |
| SwiftUI `@StateObject` | WPF + INotifyPropertyChanged + DI singleton |
| SwiftUI `@Published` | C# `private set` with `OnPropertyChanged(nameof(X))` |
| SwiftUI `HSplitView` | WPF `Grid` + `GridSplitter` |
| SwiftUI `NavigationSplitView` | (don't need — single window) |
| SwiftUI `.onDrop` | `UIElement.AllowDrop = true` + `Drop` event handler |
| SwiftUI `.commands` (menu) | `<Menu>` element with `<MenuItem>` children |
| `keyboardShortcut("o", modifiers: [.command])` | `InputBindings` with `KeyGesture(Key.O, ModifierKeys.Control)` |

---

## Appendix B: Build & development checklist for the implementer

1. Install Visual Studio 2022 17.8+ with workloads: .NET Desktop Development, Universal Windows Platform (for MSIX), WebView2 SDK.
2. `dotnet new sln -n ITforceMarkdownPro` then `dotnet new wpf -n App -o src/App`, add packages:
   - `Microsoft.Web.WebView2`
   - `AvalonEdit`
   - `DocumentFormat.OpenXml`
   - `Microsoft.Extensions.Hosting`
   - `Microsoft.Extensions.DependencyInjection`
   - `Microsoft.VisualBasic` (for Recycle Bin delete)
   - `System.Text.Json` (built-in in .NET 8)
3. Add a Windows Application Packaging Project.
4. Implement in this order (work backwards from acceptance criteria §18):
   - Models (5 records)
   - PersistenceService + JSON schemas
   - MarkdownEngine port (the big one — write tests against a corpus of .md files comparing output to expected fixtures)
   - HTML templates as embedded resources
   - WorkspaceService (file system scan)
   - AppState + INotifyPropertyChanged
   - MainWindow layout (Grid + GridSplitter)
   - WorkspaceSidebar view
   - DocumentHeader view
   - EditorPanel view + WebView2 wiring (Read mode first, then Edit, then Source)
   - Outline + filter
   - Menus + shortcuts
   - Export PDF
   - Print
   - Export Word
   - Drag-drop
   - Single-instance + file association
   - MSIX packaging

5. Run the acceptance checklist §18 manually before any release build.

---

**End of spec.** Total: ~2000 lines. If the implementing AI needs clarification on any section, the macOS source is available at `/Users/chungu/MarkdownDocsMac/` for reference — `MarkdownEngine.swift` (700 lines) is the most critical file to study before starting §7. Good luck.
