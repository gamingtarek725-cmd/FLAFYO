from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src" / "Flafyo.App" / "Assets"
ASSETS.mkdir(parents=True, exist_ok=True)
S = 512

# Deep navy rounded-square base with a subtle cyan-to-violet rim.
base = Image.new("RGBA", (S, S), (0, 0, 0, 0))
mask = Image.new("L", (S, S), 0)
md = ImageDraw.Draw(mask)
md.rounded_rectangle((20, 20, S - 20, S - 20), radius=112, fill=255)

bg = Image.new("RGBA", (S, S))
px = bg.load()
for y in range(S):
    for x in range(S):
        t = max(0.0, min(1.0, (x * 0.55 + y * 0.45) / (S - 1)))
        px[x, y] = (int(8 + 24 * t), int(13 + 4 * t), int(31 + 50 * t), 255)
base.paste(bg, (0, 0), mask)

# Soft neon halo behind the mark.
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
gd = ImageDraw.Draw(glow)
gd.polygon([(118, 106), (390, 106), (354, 178), (211, 178),
            (191, 231), (330, 231), (296, 301), (166, 301),
            (126, 397), (55, 397)], fill=(48, 178, 255, 210))
glow = glow.filter(ImageFilter.GaussianBlur(28))
base.alpha_composite(glow)

# Stylized FLAFYO F mark with a cyan-purple gradient.
mark = Image.new("L", (S, S), 0)
d = ImageDraw.Draw(mark)
points = [(118, 92), (405, 92), (369, 166), (218, 166),
          (197, 224), (337, 224), (302, 294), (171, 294),
          (128, 408), (48, 408), (92, 284), (139, 160)]
d.polygon(points, fill=255)
grad = Image.new("RGBA", (S, S))
gp = grad.load()
for y in range(S):
    for x in range(S):
        t = max(0.0, min(1.0, (x + y * 0.5) / (S * 1.5)))
        gp[x, y] = (int(28 + 112 * t), int(210 - 180 * t), int(255 - 12 * t), 255)
base.paste(grad, (0, 0), mark)

# Minimal controller silhouette integrated into the lower-right of the F.
controller = Image.new("RGBA", (S, S), (0, 0, 0, 0))
cd = ImageDraw.Draw(controller)
cd.rounded_rectangle((220, 314, 458, 430), radius=55, fill=(232, 240, 255, 255),
                     outline=(112, 147, 255, 255), width=8)
cd.ellipse((236, 368, 281, 413), fill=(21, 31, 61, 255))
cd.rectangle((251, 376, 266, 405), fill=(232, 240, 255, 255))
cd.rectangle((243, 383, 274, 398), fill=(232, 240, 255, 255))
cd.ellipse((365, 345, 397, 377), fill=(117, 73, 255, 255))
cd.ellipse((404, 376, 436, 408), fill=(34, 190, 255, 255))
base.alpha_composite(controller)

# Add a crisp rim and save both the taskbar icon and the in-app brand mark.
rim = Image.new("RGBA", (S, S), (0, 0, 0, 0))
rd = ImageDraw.Draw(rim)
rd.rounded_rectangle((20, 20, S - 20, S - 20), radius=112,
                     outline=(92, 145, 255, 230), width=5)
base.alpha_composite(rim)
base.save(ASSETS / "flafyo-brand.png", optimize=True)
base.save(ASSETS / "flafyo.ico", format="ICO",
          sizes=[(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)])
print("Generated Assets/flafyo.ico and Assets/flafyo-brand.png")
