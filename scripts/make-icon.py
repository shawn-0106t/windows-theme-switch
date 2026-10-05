"""生成应用图标 app.ico（半明半暗圆点 + 强调色圆环，与托盘运行时图标同款设计）。

一次性脚本：产物 src/ThemeSwitcher/app.ico 入库后，本脚本保留备追溯。
用法：PYTHONUTF8=1 python scripts/make-icon.py
"""
from pathlib import Path

from PIL import Image, ImageDraw

OUTPUT = Path(__file__).resolve().parent.parent / "src" / "ThemeSwitcher" / "app.ico"

SIZE = 256
DARK = (31, 31, 31, 255)
LIGHT = (243, 243, 243, 255)
ACCENT = (0, 120, 212, 255)

img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
draw = ImageDraw.Draw(img)
box = [16, 16, SIZE - 16, SIZE - 16]
draw.ellipse(box, fill=DARK)
draw.pieslice(box, 90, 270, fill=LIGHT)  # 左半圆为浅色
draw.ellipse(box, outline=ACCENT, width=14)

OUTPUT.parent.mkdir(parents=True, exist_ok=True)
img.save(
    OUTPUT,
    format="ICO",
    sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
)
print(f"written: {OUTPUT}")
