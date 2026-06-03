#!/usr/bin/env python3
"""Convert macOS app icon PNGs into multi-resolution Windows .ico files.

Outputs:
  src/ITforceMarkdown/Assets/AppIcon.ico        (Local, 朴素米色文档主题)
  src/ITforceMarkdown/Assets/AppIcon.Pro.ico    (Pro, 高级深色 / 金色主题)
"""
from PIL import Image
from pathlib import Path
import sys

LOCAL_PNGS = "/Users/chungu/ITforceMarkdown/Sources/MarkdownDocsMac/Assets.xcassets/AppIcon.appiconset"
PRO_PNGS   = "/Users/chungu/MarkdownDocsMac/Sources/MarkdownDocsMac/Assets.xcassets/AppIcon.appiconset"
OUT_DIR    = "/Users/chungu/ITforceMarkdown.Windows/src/ITforceMarkdown/Assets"

# Windows ICO 规范支持的尺寸; 嵌套这几个让 Explorer / 任务栏 / Alt-Tab 都好看
ICO_SIZES = [256, 128, 64, 48, 32, 16]

def find_png(folder, target_size):
    """Find an icon_XXX.png that matches target size, or fallback to largest."""
    folder = Path(folder)
    exact = folder / f"icon_{target_size}.png"
    if exact.exists():
        return exact
    candidates = sorted(folder.glob("icon_*.png"),
                        key=lambda p: int(p.stem.split("_")[1]) if p.stem.split("_")[1].isdigit() else 0,
                        reverse=True)
    return candidates[0] if candidates else None

def build_ico(png_folder, out_path):
    images = []
    for size in ICO_SIZES:
        src = find_png(png_folder, size)
        if not src:
            print(f"  ⚠️ no source for {size}", file=sys.stderr)
            continue
        img = Image.open(src).convert("RGBA")
        if img.size != (size, size):
            img = img.resize((size, size), Image.LANCZOS)
        images.append(img)
    if not images:
        raise SystemExit(f"No images for {png_folder}")
    # Pillow saves the first image and the `sizes` parameter as embedded entries
    out = Path(out_path)
    out.parent.mkdir(parents=True, exist_ok=True)
    images[0].save(out, format="ICO",
                   sizes=[(s, s) for s in ICO_SIZES if find_png(png_folder, s)])
    print(f"  ✓ {out}  ({len(images)} embedded sizes)")

print(f"Local icon  ({LOCAL_PNGS}):")
build_ico(LOCAL_PNGS, f"{OUT_DIR}/AppIcon.ico")

print(f"Pro icon    ({PRO_PNGS}):")
build_ico(PRO_PNGS, f"{OUT_DIR}/AppIcon.Pro.ico")
