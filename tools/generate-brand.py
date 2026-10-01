"""Build Ferry's SVG, XAML and raster assets from the same ribbon geometry.

Python dependencies: fonttools, pillow, resvg-py (see docs/brand/README.md).
No dependency is added to the application. --check verifies generated vectors.
"""

from __future__ import annotations

import argparse
import io
import math
from pathlib import Path
import struct
import sys
from xml.sax.saxutils import escape

from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parents[1]
BRAND = ROOT / "web/ui/brand"
DURATION = 2.6
PAUSE_MS = 2400
TRACE_LENGTH = 420
TRACE = "M80 202 V86 Q80 68 100 68 H184 L126 126 H151 L95 178"

# One ribbon: upright, crown, return fold, middle and the lower return.
# The blue returns sit behind the pearl faces, so the silhouette stays a clear F.
SHAPES = {
    "stem": "M68 78 L108 49 V181 Q108 185 104 188 L75 208 Q68 212 68 204 Z",
    "fold": "M108 88 H158 Q162 88 166 84 L134 116 H108 Z",
    "crown": "M68 78 L100 51 Q104 48 110 48 H193 Q201 48 196 54 L167 83 Q162 88 157 88 H82 Z",
    "return": "M94 148 H134 L108 174 V181 Q108 185 104 188 L75 208 Q68 212 68 204 V174 Z",
    "middle": "M68 149 L100 119 Q104 116 110 116 H158 Q166 116 160 122 L137 145 Q134 148 129 148 H94 L68 174 Z",
}
FILLS = {"stem": "pearl", "crown": "pearl", "middle": "pearl", "fold": "blue", "return": "blue"}

# time, opacity, x, y, scale-x, scale-y, rotation. The same keyframes are used
# in CSS, the native WPF control and deterministic GIF exports.
TRACKS = {
    "stem": [(0, 0, -10, 30, .08, 1.08, 0), (.24, 0, -10, 30, .08, 1.08, 0), (.72, 1, 0, 0, 1, 1, 0)],
    "fold": [(0, 0, -8, -14, 1, .2, 0), (.62, 0, -8, -14, 1, .2, 0), (1.02, 1, 0, 0, 1, 1, 0)],
    "crown": [(0, 0, -26, -18, .6, .8, -12), (.38, 0, -26, -18, .6, .8, -12), (.96, 1, 2, 0, 1, 1, .5), (1.1, 1, 0, 0, 1, 1, 0)],
    "return": [(0, 0, -8, -8, 1, .5, 0), (.96, 0, -8, -8, 1, .5, 0), (1.4, 1, 0, 0, 1, 1, 0)],
    "middle": [(0, 0, -22, 10, .65, .8, 10), (.72, 0, -22, 10, .65, .8, 10), (1.18, 1, 1.5, 0, 1, 1, -.4), (1.32, 1, 0, 0, 1, 1, 0)],
}


def number(value: float) -> str:
    return f"{value:.3f}".rstrip("0").rstrip(".") or "0"


def wordmark() -> tuple[str, int]:
    """Outline the bundled OFL Geist; adjust spacing for this particular word."""
    with TTFont(ROOT / "app/fonts/Geist-SemiBold.ttf") as font:
        glyphs = font.getGlyphSet()
        cmap = font.getBestCmap()
        scale = 142 / font["head"].unitsPerEm
        pen = SVGPathPen(glyphs, ntos=number)
        advance = 0
        pairs = {"Fe": -38, "er": -12, "rr": -12, "ry": -38}
        for index, letter in enumerate("Ferry"):
            if index:
                advance += pairs.get("Ferry"[index - 1:index + 1], 0) - 15
            name = cmap[ord(letter)]
            if index:
                glyphs[name].draw(TransformPen(pen, (scale, 0, 0, -scale, 5 + advance * scale, 114)))
            advance += font["hmtx"][name][0]
        # The initial F repeats the ribbon's diagonal terminals; the rest keeps
        # Geist's open counters. These are outlines, not a font dependency.
        initial = "M13 12 H80 L66 26 H33 V55 H72 L58 69 H33 V114 H13 Z"
        return initial + pen.getCommands(), math.ceil(advance * scale + 10)


WORD, WORD_WIDTH = wordmark()
LOGO_WIDTH = 278 + math.ceil(WORD_WIDTH * 1.13)


def definitions(prefix: str = "ferry", light: bool = False) -> str:
    pearl = ("#253449", "#162234", "#0D1522") if light else ("#FFFFFF", "#EEF2F8", "#CDD8E8")
    return f'''<defs>
  <linearGradient id="{prefix}-pearl" x1="68" y1="48" x2="152" y2="208" gradientUnits="userSpaceOnUse">
    <stop stop-color="{pearl[0]}"/><stop offset=".5" stop-color="{pearl[1]}"/><stop offset="1" stop-color="{pearl[2]}"/>
  </linearGradient>
  <linearGradient id="{prefix}-blue" x1="155" y1="85" x2="72" y2="205" gradientUnits="userSpaceOnUse">
    <stop stop-color="#83B4FF"/><stop offset=".42" stop-color="#527FE8"/><stop offset="1" stop-color="#2852AC"/>
  </linearGradient>
  <linearGradient id="{prefix}-badge" x1="32" y1="8" x2="230" y2="248" gradientUnits="userSpaceOnUse">
    <stop stop-color="#1A2535"/><stop offset=".55" stop-color="#101824"/><stop offset="1" stop-color="#0A0D12"/>
  </linearGradient>
  <linearGradient id="{prefix}-shine">
    <stop stop-color="#80DFFF" stop-opacity="0"/><stop offset=".47" stop-color="#C7F4FF" stop-opacity=".1"/>
    <stop offset=".6" stop-color="#FFFFFF" stop-opacity=".72"/><stop offset="1" stop-color="#50E6FF" stop-opacity="0"/>
  </linearGradient>
  <clipPath id="{prefix}-silhouette">{''.join(f'<path d="{path}"/>' for path in SHAPES.values())}</clipPath>
</defs>'''


def badge(prefix: str) -> str:
    return f'''<rect x="8" y="8" width="240" height="240" rx="57" fill="url(#{prefix}-badge)"/>
<rect x="8.75" y="8.75" width="238.5" height="238.5" rx="56.25" fill="none" stroke="#8AA6CF" stroke-opacity=".13" stroke-width="1.5"/>'''


def transform(state: tuple[float, ...]) -> str:
    _, x, y, sx, sy, angle = state
    return f"translate({number(x)} {number(y)}) translate(128 128) rotate({number(angle)}) scale({number(sx)} {number(sy)}) translate(-128 -128)"


def interpolate(frames: list[tuple], at: float) -> tuple[float, ...]:
    if at <= frames[0][0]:
        return tuple(frames[0][1:])
    for left, right in zip(frames, frames[1:]):
        if at <= right[0]:
            progress = 1 - (1 - (at - left[0]) / (right[0] - left[0])) ** 3
            return tuple(a + (b - a) * progress for a, b in zip(left[1:], right[1:]))
    return tuple(frames[-1][1:])


def faces(prefix: str, at: float | None = None, mono: bool = False) -> str:
    parts = []
    for name, path in SHAPES.items():
        state = (1, 0, 0, 1, 1, 0) if at is None else interpolate(TRACKS[name], at)
        fill = "currentColor" if mono else f"url(#{prefix}-{FILLS[name]})"
        parts.append(f'<g class="ribbon-{name}" opacity="{number(state[0])}" transform="{transform(state)}"><path d="{path}" fill="{fill}"/></g>')
    # A small highlight along each fold makes depth legible without a glow filter.
    edge_opacity = 1 if at is None else interpolate([(0, 0), (1.18, 0), (1.5, 1)], at)[0]
    if not mono:
        parts.append(f'<g class="ribbon-edges" opacity="{number(edge_opacity)}" fill="none" stroke="#C3DDFF" stroke-opacity=".38" stroke-width=".85"><path d="M108 89 H156 M96 149 H129"/></g>')
    return "\n".join(parts)


def effects(prefix: str, at: float | None = None) -> str:
    trace_opacity = 0 if at is None else interpolate([(0, 0), (.06, 1), (.5, 1), (1, 0)], at)[0]
    trace_offset = TRACE_LENGTH if at is None else interpolate([(0, TRACE_LENGTH), (.82, 0)], at)[0]
    shine_opacity = 0 if at is None else interpolate([(0, 0), (1.22, 0), (1.36, 1), (2.16, 1), (2.4, 0)], at)[0]
    shine_x = -200 if at is None else interpolate([(0, -200), (1.22, -200), (2.35, 240)], at)[0]
    return f'''<path class="ribbon-filament" d="{TRACE}" fill="none" stroke="#78D9FF" stroke-width="2" stroke-linecap="round" stroke-dasharray="{TRACE_LENGTH}" stroke-dashoffset="{number(trace_offset)}" opacity="{number(trace_opacity)}"/>
<g clip-path="url(#{prefix}-silhouette)"><g class="ribbon-shine" opacity="{number(shine_opacity)}" transform="translate({number(shine_x)} 0)"><path d="M0 30 H66 L166 224 H100 Z" fill="url(#{prefix}-shine)"/></g></g>'''


def css_keyframes() -> str:
    rules = ["/* Generated by tools/generate-brand.py. Animation lasts 2.6 s, then rests. */"]
    for name, frames in TRACKS.items():
        rules.append(f"@keyframes ribbon-{name} {{")
        for frame in [*frames, (DURATION, *frames[-1][1:])]:
            _, opacity, x, y, sx, sy, angle = frame
            rules.append(f"  {number(frame[0] / DURATION * 100)}% {{ opacity: {number(opacity)}; transform: translate({number(x)}px, {number(y)}px) rotate({number(angle)}deg) scale({number(sx)}, {number(sy)}); }}")
        rules.append("}")
    rules += [
        "@keyframes ribbon-edges { 0%, 45% { opacity: 0; } 58%, 100% { opacity: 1; } }",
        f"@keyframes ribbon-filament {{ 0% {{ opacity: 0; stroke-dashoffset: {TRACE_LENGTH}; }} 3% {{ opacity: 1; }} 19% {{ opacity: 1; }} 32% {{ stroke-dashoffset: 0; }} 39%, 100% {{ opacity: 0; stroke-dashoffset: 0; }} }}",
        "@keyframes ribbon-shine { 0%, 47% { opacity: 0; transform: translateX(-200px); } 53% { opacity: 1; } 83% { opacity: 1; } 91% { transform: translateX(240px); opacity: 0; } 100% { transform: translateX(240px); opacity: 0; } }",
        "@keyframes ribbon-word { 0%, 63% { opacity: 0; transform: translateX(12px); clip-path: inset(0 100% 0 0); } 91%, 100% { opacity: 1; transform: translateX(0); clip-path: inset(0); } }",
        ".ribbon-filament, .ribbon-shine { opacity: 0; }",
    ]
    for name in [*TRACKS, "edges", "filament", "shine"]:
        rules.append(f".brand-enter .ribbon-{name} {{ animation: ribbon-{name} 2.6s cubic-bezier(.333333, 1, .666667, 1) both; transform-origin: 128px 128px; }}")
    rules += [
        ".brand-enter .brand-wordmark { animation: ribbon-word 2.6s cubic-bezier(.333333, 1, .666667, 1) both; }",
        "@keyframes ribbon-hover { 0% { opacity: 0; transform: translateX(-200px); } 20% { opacity: 1; } 80% { opacity: 1; } 100% { opacity: 0; transform: translateX(240px); } }",
        ".brand:not(.brand-enter):hover .ribbon-shine { animation: ribbon-hover .35s ease-out; }",
        "@media (prefers-reduced-motion: reduce) {",
        "  .brand-enter [class^=ribbon-], .brand-enter .brand-wordmark, .brand:hover .ribbon-shine { animation: none !important; }",
        "  .ribbon-filament, .ribbon-shine { display: none; }",
        "}",
    ]
    return "\n".join(rules) + "\n"


CSS = css_keyframes()


def svg(body: str, width: int = 256, height: int = 256, animated: bool = False, color: str = "#E9EDF3", title: str = "Ferry") -> str:
    return f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {height}" role="img" aria-labelledby="title" color="{color}"{' class="brand-enter"' if animated else ''}>
<title id="title">{escape(title)}</title>
{f'<style>{CSS}</style>' if animated else ''}
{body}
</svg>
'''


def icon(at: float | None = None, animated: bool = False, small: bool = False) -> str:
    if small:
        body = f'''{definitions()}{badge('ferry')}
<path d="M68 56 Q68 48 76 48 H192 L156 88 H108 V116 H160 L130 150 H108 V186 L68 210 Z" fill="#F2F5FA"/>
<path d="M108 88 H156 L130 114 H108 Z M108 150 H130 L108 172 Z" fill="#6F97FF"/>'''
    else:
        body = definitions() + badge("ferry") + faces("ferry", at) + (effects("ferry", at) if animated or at is not None else "")
    return svg(body, animated=animated)


def logo(at: float | None = None, animated: bool = False, mono: bool = False, color: str = "#E9EDF3", light: bool = False) -> str:
    mark = faces("ferry", at, mono)
    word_opacity = 1 if at is None else interpolate([(0, 0), (1.64, 0), (2.34, 1)], at)[0]
    word_x = 0 if at is None else interpolate([(0, 12), (1.64, 12), (2.34, 0)], at)[0]
    revealed = WORD_WIDTH if at is None else WORD_WIDTH * interpolate([(0, 0), (1.64, 0), (2.34, 1)], at)[0]
    clip = f'<defs><clipPath id="word-reveal"><rect width="{number(revealed)}" height="148"/></clipPath></defs>' if at is not None else ""
    body = definitions(light=light) + mark + (effects("ferry", at) if animated or at is not None else "")
    body += f'''<g transform="translate(278 43) scale(1.13)">{clip}<g class="brand-wordmark" transform="translate({number(word_x)} 0)" opacity="{number(word_opacity)}"{' clip-path="url(#word-reveal)"' if at is not None else ''}><path d="{WORD}" fill="currentColor"/></g></g>'''
    return svg(body, LOGO_WIDTH, 256, animated, color)


def fragment(prefix: str) -> str:
    return f'''<svg class="logo" viewBox="0 0 256 256" aria-hidden="true" focusable="false">
{definitions(prefix)}{badge(prefix)}{faces(prefix)}{effects(prefix)}
</svg><div class="brand-copy"><img class="brand-wordmark" src="brand/ferry-wordmark.svg" alt="Ferry" width="{WORD_WIDTH}" height="148"><small><span data-i18n="ui.tagline">envio de jogos para PS5</span></small></div>'''


def xaml_geometry() -> str:
    names = {"stem": "Stem", "fold": "Fold", "crown": "Crown", "return": "Return", "middle": "Middle"}
    parts = [f'<Geometry x:Key="Ferry{names[name]}">{path}</Geometry>' for name, path in SHAPES.items()]
    parts += [f'<Geometry x:Key="FerryWordmark">{WORD}</Geometry>', f'<Geometry x:Key="FerryFilament">{TRACE}</Geometry>', f'<Geometry x:Key="FerrySilhouette">{ " ".join(SHAPES.values())}</Geometry>']
    def animation(target: str, prop: str, frames: list[tuple]) -> str:
        keys = []
        for at, value in [*frames, (DURATION, frames[-1][1])]:
            keys.append(f'<EasingDoubleKeyFrame KeyTime="0:0:{number(at)}" Value="{number(value)}"><EasingDoubleKeyFrame.EasingFunction><CubicEase EasingMode="EaseOut"/></EasingDoubleKeyFrame.EasingFunction></EasingDoubleKeyFrame>')
        return f'<DoubleAnimationUsingKeyFrames Storyboard.TargetName="{target}" Storyboard.TargetProperty="{prop}" Duration="0:0:2.6" FillBehavior="Stop">' + "".join(keys) + '</DoubleAnimationUsingKeyFrames>'

    motions = []
    for name, frames in TRACKS.items():
        native = names[name]
        for target, prop, index in ((native + "Group", "Opacity", 1), (native + "Offset", "X", 2), (native + "Offset", "Y", 3), (native + "Scale", "ScaleX", 4), (native + "Scale", "ScaleY", 5), (native + "Rotation", "Angle", 6)):
            motions.append(animation(target, prop, [(frame[0], frame[index]) for frame in frames]))
    motions += [
        animation("Edges", "Opacity", [(0, 0), (1.18, 0), (1.5, 1)]),
        animation("Filament", "Opacity", [(0, 0), (.06, 1), (.5, 1), (1, 0)]),
        animation("Filament", "StrokeDashOffset", [(0, TRACE_LENGTH / 2), (.82, 0)]),
        animation("Shine", "Opacity", [(0, 0), (1.22, 0), (1.36, 1), (2.16, 1), (2.4, 0)]),
        animation("ShineOffset", "X", [(0, -200), (1.22, -200), (2.35, 240)]),
        animation("Wordmark", "Opacity", [(0, 0), (1.64, 0), (2.34, 1)]),
        animation("WordmarkOffset", "X", [(0, 12), (1.64, 12), (2.34, 0)]),
        animation("WordmarkReveal", "ScaleX", [(0, 0), (1.64, 0), (2.34, 1)]),
    ]
    parts.append('<Storyboard x:Key="FerryBrandEnter">' + "\n".join(motions) + '</Storyboard>')
    return '''<!-- Generated by tools/generate-brand.py; shared SVG ribbon and Geist outlines. -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
''' + "\n".join(parts) + "\n</ResourceDictionary>\n"


def generated() -> dict[Path, str]:
    return {
        BRAND / "ferry-symbol.svg": svg(definitions() + faces("ferry")),
        BRAND / "ferry-symbol-mono.svg": svg(faces("ferry", mono=True)),
        BRAND / "ferry-icon.svg": icon(),
        BRAND / "ferry-icon-small.svg": icon(small=True),
        BRAND / "ferry-icon-animated.svg": icon(animated=True),
        BRAND / "ferry-wordmark.svg": svg(f'<path id="wordmark" d="{WORD}" fill="currentColor"/>', WORD_WIDTH, 148),
        BRAND / "ferry-logo.svg": logo(),
        BRAND / "ferry-logo-light.svg": logo(color="#101824", light=True),
        BRAND / "ferry-logo-mono.svg": logo(mono=True),
        BRAND / "ferry-logo-animated.svg": logo(animated=True),
        BRAND / "brand.css": CSS,
        ROOT / "web/ui/logo.svg": icon(small=True),
        ROOT / "app/BrandGeometry.xaml": xaml_geometry(),
    }


def render_svg(source: str, width: int, height: int):
    from PIL import Image
    import resvg_py

    source = source.replace('<svg ', f'<svg width="{width}" height="{height}" ', 1)
    return Image.open(io.BytesIO(resvg_py.svg_to_bytes(svg_string=source))).convert("RGBA")


def render_assets() -> None:
    from PIL import Image, ImageDraw, ImageFont

    render_svg(icon(), 512, 512).save(ROOT / "docs/logo.png")
    images = [render_svg(icon(small=size <= 32), size * 4, size * 4).resize((size, size), Image.Resampling.LANCZOS) for size in (16, 24, 32, 48, 64, 128, 256)]
    # Write the seven native ICO frames separately; Pillow's default ICO writer
    # would rescale all small frames from the full-detail 256 px image.
    data = []
    for image in images:
        stream = io.BytesIO()
        image.save(stream, format="PNG")
        data.append(stream.getvalue())
    offset = 6 + 16 * len(images)
    entries = []
    for image, png in zip(images, data):
        size = image.width if image.width < 256 else 0
        entries.append(struct.pack("<BBBBHHII", size, size, 0, 0, 1, 32, len(png), offset))
        offset += len(png)
    (ROOT / "app/Ferry.ico").write_bytes(struct.pack("<HHH", 0, 1, len(images)) + b"".join(entries) + b"".join(data))

    def gif(path: Path, width: int, height: int, builder) -> None:
        rgb_frames = []
        for index in range(131):
            frame = render_svg(builder(min(index * .02, DURATION)), width, height)
            background = Image.new("RGBA", frame.size, "#0A0D12")
            background.alpha_composite(frame)
            rgb_frames.append(background.convert("RGB"))
        # One palette for the entire sequence prevents flicker between frames.
        atlas = Image.new("RGB", (width * 6, height * 4))
        for index in range(24):
            atlas.paste(rgb_frames[round(index * 130 / 23)], ((index % 6) * width, (index // 6) * height))
        palette = atlas.quantize(colors=255, method=Image.Quantize.MEDIANCUT)
        frames = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in rgb_frames]
        frames[0].save(path, save_all=True, append_images=frames[1:], duration=[20] * 130 + [PAUSE_MS], loop=0, disposal=1, optimize=True)

    gif(ROOT / "docs/logo_animated.gif", 384, 384, lambda at: icon(at=at))
    gif(ROOT / "docs/brand/ferry-logo-animated.gif", LOGO_WIDTH, 256, lambda at: logo(at=at))

    # Review sheet: icon, full logo, native sizes and five formation stages.
    board = Image.new("RGBA", (1440, 1000), "#0A0D12")
    draw = ImageDraw.Draw(board)
    font = ImageFont.truetype(str(ROOT / "app/fonts/Geist-Regular.ttf"), 20)
    draw.text((56, 38), "FERRY / FITA DOBRADA", font=font, fill="#8DA2BE")
    board.alpha_composite(render_svg(icon(), 340, 340), (48, 105))
    board.alpha_composite(render_svg(logo(), LOGO_WIDTH, 256), (475, 130))
    draw.text((72, 490), "16 / 24 / 32 / 48 / 64 / 128 px", font=font, fill="#8DA2BE")
    x = 72
    for size in (16, 24, 32, 48, 64, 128):
        board.alpha_composite(render_svg(icon(small=size <= 32), size, size), (x, 550 - size // 2))
        x += size + 34
    for index, at in enumerate((.3, .65, 1.05, 1.65, 2.6)):
        board.alpha_composite(render_svg(icon(at=at), 220, 220), (56 + index * 272, 690))
        draw.text((70 + index * 272, 940), f"{at:.2f} s", font=font, fill="#8DA2BE")
    board.save(ROOT / "docs/brand/preview.png")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Verify generated text assets without writes.")
    parser.add_argument("--vectors-only", action="store_true", help="Skip PNG, ICO, GIF and the review sheet.")
    args = parser.parse_args()
    # A task-local dependency install is optional; no global environment change.
    local_dependencies = ROOT / "dist-brand/python"
    if local_dependencies.is_dir():
        sys.path.insert(0, str(local_dependencies))
    files = generated()
    index = ROOT / "web/ui/index.html"
    html = index.read_text(encoding="utf-8")
    for name in ("auth", "shell"):
        start, end = f"<!-- ferry-brand-{name}:start -->", f"<!-- ferry-brand-{name}:end -->"
        if start in html:
            before, rest = html.split(start, 1)
            _, after = rest.split(end, 1)
            html = before + start + fragment("ferry-" + name) + end + after
    files[index] = html
    if args.check:
        stale = [str(path.relative_to(ROOT)) for path, content in files.items() if not path.exists() or path.read_text(encoding="utf-8") != content]
        if stale:
            raise SystemExit("Stale generated assets: " + ", ".join(stale))
        print(f"PASS: {len(files)} generated vector/integration files match the canonical geometry.")
        return
    for path, content in files.items():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8", newline="\n")
    (ROOT / "docs/brand").mkdir(parents=True, exist_ok=True)
    if not args.vectors_only:
        render_assets()
    print(f"Generated {len(files)} vector/integration files; wordmark {WORD_WIDTH} x 148; logo {LOGO_WIDTH} x 256.")


if __name__ == "__main__":
    main()
