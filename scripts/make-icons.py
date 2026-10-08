# -*- coding: utf-8 -*-
"""生成「内网测速」全套图标：主图标（速度表盘）+ 测速中变体（绿徽章双向箭头）。
1024px 超采样绘制，LANCZOS 高质量缩放到各尺寸（exe/任务栏/MSIX/托盘）。"""
import math
from PIL import Image, ImageDraw, ImageOps

S = 1024  # 超采样画布
BRAND_TOP = (0, 122, 204)     # #007ACC
BRAND_BOTTOM = (0, 90, 158)   # #005A9E
WHITE = (255, 255, 255, 255)
WHITE_60 = (255, 255, 255, 153)
GREEN = (0, 180, 42, 255)     # #00B42A


def vertical_gradient(size, top, bottom):
    """垂直线性渐变 RGBA 图。"""
    grad = Image.linear_gradient('L').rotate(90, expand=True).resize((size, size))
    grad = ImageOps.invert(grad)  # 上亮下暗
    r = Image.new('L', (size, 1))
    for x in range(size):
        r.putpixel((x, 0), int(top[0] + (bottom[0] - top[0]) * x / (size - 1)))
    g = Image.new('L', (size, 1))
    for x in range(size):
        g.putpixel((x, 0), int(top[1] + (bottom[1] - top[1]) * x / (size - 1)))
    b = Image.new('L', (size, 1))
    for x in range(size):
        b.putpixel((x, 0), int(top[2] + (bottom[2] - top[2]) * x / (size - 1)))
    out = Image.merge('RGB', (r, g, b)).resize((size, size))
    return out


def rounded_base(radius_ratio=0.215):
    """渐变圆角方形底。"""
    mask = Image.new('L', (S, S), 0)
    d = ImageDraw.Draw(mask)
    d.rounded_rectangle([0, 0, S, S], radius=int(S * radius_ratio), fill=255)
    base = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    grad = vertical_gradient(S, BRAND_TOP, BRAND_BOTTOM)
    base.paste(grad, (0, 0), mask)
    return base


def draw_gauge(img):
    """白色速度表盘：粗弧 + 刻度 + 右上指针 + 中心点。"""
    d = ImageDraw.Draw(img)
    cx, cy = S * 0.5, S * 0.60
    R = S * 0.335
    # 表盘弧：从 150° 到 30°（越过顶部，PIL 角度顺时针、0°=东）
    d.arc([cx - R, cy - R, cx + R, cy + R], start=150, end=390, fill=WHITE, width=int(S * 0.055))
    # 刻度：5 条沿弧的短径向线
    for deg in (150, 195, 240, 285, 330):
        a = math.radians(deg)
        r1, r2 = R * 0.80, R * 0.64
        x1, y1 = cx + r1 * math.cos(a), cy + r1 * math.sin(a)
        x2, y2 = cx + r2 * math.cos(a), cy + r2 * math.sin(a)
        d.line([x1, y1, x2, y2], fill=WHITE, width=int(S * 0.030))
    # 指针：指向右上（-45°），楔形
    a = math.radians(-45)
    tip_r = R * 0.62
    tx, ty = cx + tip_r * math.cos(a), cy + tip_r * math.sin(a)
    half_w = S * 0.026
    px, py = -math.sin(a), math.cos(a)  # 垂直方向
    tail = S * 0.055
    bx, by = cx - tail * math.cos(a), cy - tail * math.sin(a)
    d.polygon([(tx, ty), (bx + half_w * px, by + half_w * py), (bx - half_w * px, by - half_w * py)], fill=WHITE)
    # 中心轴点
    r0 = S * 0.036
    d.ellipse([cx - r0, cy - r0, cx + r0, cy + r0], fill=WHITE)


def draw_busy_badge(img):
    """右下角绿色徽章 + 白色双向箭头 ⇅（测速中状态）。"""
    d = ImageDraw.Draw(img)
    bx, by, br = S * 0.735, S * 0.735, S * 0.235
    # 白色描边圈（与底分离）
    d.ellipse([bx - br - S * 0.018, by - br - S * 0.018, bx + br + S * 0.018, by + br + S * 0.018], fill=WHITE)
    d.ellipse([bx - br, by - br, bx + br, by + br], fill=GREEN)
    # 双向箭头 ⇅（上下两个三角 + 中间双杆）
    w, h = br * 0.42, br * 0.62
    # 上箭头
    d.polygon([(bx, by - h * 0.78), (bx - w, by - h * 0.18), (bx + w, by - h * 0.18)], fill=WHITE)
    # 下箭头
    d.polygon([(bx, by + h * 0.78), (bx - w, by + h * 0.18), (bx + w, by + h * 0.18)], fill=WHITE)
    # 两根竖杆
    bar_w = br * 0.13
    for off in (-w * 0.5, w * 0.5):
        d.rectangle([bx + off - bar_w / 2, by - h * 0.12, bx + off + bar_w / 2, by + h * 0.12], fill=WHITE)


def make(base_fn, busy=False):
    img = base_fn()
    draw_gauge(img)
    if busy:
        draw_busy_badge(img)
    return img


def save_pngs(img, name, sizes, outdir):
    for sz in sizes:
        img.resize((sz, sz), Image.LANCZOS).save(f'{outdir}/{name}-{sz}.png')


def save_ico(img, path, sizes):
    """手动构造多尺寸 ICO（PNG-in-ICO）：Pillow 的 ICO 保存参数不可靠，之前只写出了 16x16 单条目。"""
    import struct
    from io import BytesIO
    pngs = []
    for s in sizes:
        buf = BytesIO()
        img.resize((s, s), Image.LANCZOS).save(buf, format='PNG')
        pngs.append(buf.getvalue())
    header = struct.pack('<HHH', 0, 1, len(sizes))
    offset = 6 + 16 * len(sizes)
    entries = b''
    data = b''
    for s, png in zip(sizes, pngs):
        b = 0 if s >= 256 else s
        entries += struct.pack('<BBBBHHII', b, b, 0, 0, 1, 32, len(png), offset)
        data += png
        offset += len(png)
    with open(path, 'wb') as f:
        f.write(header + entries + data)


if __name__ == '__main__':
    import os
    out = r'C:\Users\itjun\Documents\iLearn\itjun-speed\src\LanSpeed.App\Assets'
    os.makedirs(out, exist_ok=True)
    normal = make(rounded_base, busy=False)
    busy = make(rounded_base, busy=True)

    # 应用图标（exe / 任务栏 / 托盘常态）
    save_ico(normal, f'{out}\\app.ico', [16, 24, 32, 48, 64, 128, 256])
    # 测速中托盘图标
    save_ico(busy, f'{out}\\app-busy.ico', [16, 24, 32, 48, 64])
    # MSIX 徽标（44/150 由 256 高质量缩放）
    normal.resize((44, 44), Image.LANCZOS).save(f'{out}\\Square44x44Logo.png')
    normal.resize((150, 150), Image.LANCZOS).save(f'{out}\\Square150x150Logo.png')
    normal.resize((256, 256), Image.LANCZOS).save(f'{out}\\app-256.png')
    busy.resize((256, 256), Image.LANCZOS).save(f'{out}\\app-busy-256.png')
    print('icons generated in', out)
