# ITforce Markdown — Windows

Windows 端 (WPF + .NET 8 + WebView2 + Markdig) 的源代码仓库。

**当前状态：0.1.0 骨架**。能编译、能启动、能跑 CI，但功能区还是空的。功能实现见 [`WINDOWS_PORT_SPEC.md`](WINDOWS_PORT_SPEC.md)（完整设计文档，可以丢给别的 AI 让它照着填）。

---

## 你现在要做的事

**目标**：把这个文件夹推到一个新的 GitHub 仓库，让 GitHub Actions 自动编译出 Windows `.exe`，你下载下来双击就能运行。整个过程不需要 Windows 电脑。

下面分四步，照着抄就行。

---

## Step 1 — 在 GitHub 上建一个空仓库

1. 浏览器打开 https://github.com/new
2. 填表：
   - **Repository name**: `ITforceMarkdown-Windows`（或者你喜欢的名字）
   - **Description**: 随便写，比如 "Windows port of ITforce Markdown"
   - **Public / Private**: 私有就够了（Private）。Actions 对私有仓库每月有 2000 分钟免费配额，编译一次大概 3 分钟，完全够用。
   - **不要**勾选 "Add a README file"、"Add .gitignore"、"Choose a license"。这些我们本地都有了，勾了会冲突。
3. 点 **Create repository**。
4. 创建完会出现一个页面，最上面有个仓库 URL，类似 `git@github.com:你的用户名/ITforceMarkdown-Windows.git`。**这个 URL 待会要用，先放着别关页面**。

---

## Step 2 — 把本地代码推上去

打开 macOS 的 **Terminal**，逐行执行（每行回车）：

```bash
cd /Users/chungu/ITforceMarkdown.Windows

# 初始化 git 仓库
git init
git add .
git commit -m "Initial commit: WPF skeleton + GitHub Actions"

# 关联到你刚创建的远端仓库
# ⚠️ 把下面这行的 URL 换成 Step 1 页面上看到的那个
git remote add origin git@github.com:你的用户名/ITforceMarkdown-Windows.git

# main 分支推上去
git branch -M main
git push -u origin main
```

如果你之前没在这台电脑用过 SSH push GitHub，会提示认证失败。两种解决方法（任选一种）：

**方法 A：用 HTTPS（最简单）**
把 `git remote add origin` 那行的 URL 换成 HTTPS 版本，长这样：
```
git remote add origin https://github.com/你的用户名/ITforceMarkdown-Windows.git
```
然后 push 时会弹一个浏览器窗口让你登录 GitHub。

**方法 B：用 GitHub Desktop（不想碰命令行）**
1. 下载 GitHub Desktop：https://desktop.github.com
2. 登录你的 GitHub 账号
3. File → Add Local Repository → 选 `/Users/chungu/ITforceMarkdown.Windows`
4. 点 "Publish repository"，填名字，按 Publish

---

## Step 3 — 看 GitHub Actions 自动编译

1. push 完成后，浏览器打开你的仓库（`https://github.com/你的用户名/ITforceMarkdown-Windows`）
2. 顶部找 **Actions** tab，点进去
3. 应该能看到一个叫 "Build Windows" 的 workflow 已经在跑（最上面那个，有黄色圆点表示进行中，绿色对勾表示成功）
4. 点进去看实时日志。第一次大概 **3–5 分钟**，因为要下载 .NET 依赖。

如果 build 失败了（红色叉），点进去看哪一步红了，把报错截图发给我。

---

## Step 4 — 下载编译好的 .exe

**方式 A — 每次 push 都自动出一份（artifact）**

1. Actions 页面点进绿色对勾那次 run
2. 滚到页面最底部，有个 **Artifacts** section
3. 下载 `ITforceMarkdown-windows-x64-vX.X.X.zip`
4. 解压，里面的 `ITforceMarkdown.exe` 拷到任何 Windows 电脑双击就能运行
5. ⚠️ artifact 只保留 30 天，过期就没了

**方式 B — 打 tag 自动出正式 release**

每次想发版时（比如 0.2 0.3 这种里程碑），在本地：
```bash
git tag v0.1.0
git push origin v0.1.0
```
Actions 会自动建一个 GitHub Release，`.exe` 直接挂在 Releases 页面，**永久保留**。这是给最终用户下载的渠道。

---

## 运行需要什么？

`.exe` 是 self-contained 的（.NET runtime 已经打包进去），任何 **Windows 10 64-bit 1809+ 或 Windows 11** 双击就能开。

唯一外部依赖是 **WebView2 Runtime**（用来渲染 Markdown 预览）。Windows 11 自带，Windows 10 21H2+ 一般也有。万一目标机器没装，Edge 浏览器装一下就有了，或者从这里下：https://developer.microsoft.com/microsoft-edge/webview2/

---

## 项目结构

```
ITforceMarkdown.Windows/
├── .github/
│   └── workflows/
│       └── build.yml              ← GitHub Actions 配置
├── src/
│   └── ITforceMarkdown/
│       ├── ITforceMarkdown.csproj ← 项目文件 (依赖、版本号)
│       ├── App.xaml + .cs         ← 应用入口
│       ├── MainWindow.xaml + .cs  ← 主窗口 (目前是占位)
│       └── app.manifest           ← DPI + Win10/11 兼容声明
├── ITforceMarkdown.sln            ← Visual Studio 解决方案
├── WINDOWS_PORT_SPEC.md           ← 完整设计文档 ← 给 AI 看这个
├── .gitignore
└── README.md                      ← 你正在看的这份
```

---

## 给后续 AI 的提示

如果你（用户）要把这个项目交给另一个 AI 让它填功能，**给那个 AI 的 prompt 里务必带上**：

> 请阅读这个仓库根目录的 `WINDOWS_PORT_SPEC.md`，那是完整的功能、UI、架构设计文档。当前 `src/ITforceMarkdown/` 下只是一个最小骨架，请按照 spec 把 WorkspaceStore、MarkdownEngine、Sidebar、TopActionBar、DocumentWorkspace 等组件填进去。每次 push 到 main 分支都会自动通过 GitHub Actions 编译并打包成 single-file `.exe`，artifact 可以在 Actions 页面下载。

这样它就知道项目目标、架构、依赖、Mac 版的对应行为，不会瞎写。

---

## 排错小抄

| 问题 | 解决 |
| --- | --- |
| `git push` 报权限错误 | 见 Step 2 方法 A：换 HTTPS URL，浏览器认证 |
| Actions 红色叉，提示 NuGet restore 失败 | NuGet 镜像偶发抽风，等 5 分钟，再点 "Re-run all jobs" |
| Actions 提示 "no workflow file" | 检查 `.github/workflows/build.yml` 是否真的 push 上去了（在仓库网页点这个路径） |
| `.exe` 在 Windows 上提示 "无法打开，因为来源不明" | 右键 → 属性 → 勾 "解除阻止"，再双击。或者鼠标右键 → 打开 → 仍要打开。（没有代码签名证书时 SmartScreen 会拦） |
| WebView2 报错 | 目标机器装 Edge 浏览器（或 WebView2 Runtime），就有了 |

---

## 接下来

骨架就绪后，下一步：

1. ✅ Verify build passes (Step 3 看到绿色对勾)
2. ⏳ 找另一个 AI 按 `WINDOWS_PORT_SPEC.md` 实现功能
3. ⏳ 在自己的 Windows 机器 / VM 上测一遍（或者直接发给用户测）
4. ⏳ 申请 EV 代码签名证书消除 SmartScreen 警告（可选，几百刀一年）
5. ⏳ 提交到 Microsoft Store（可选，spec 的 MSIX 章节有写）
