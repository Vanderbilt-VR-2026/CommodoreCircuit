"""Vector HUD icons for the interactables, styled to match hud-standings-badges.png:
black disc, thick glossy Bright Gold rim, bold black outer stroke.

Readability rule carried over from the 3D models:
  hazards   -> white subject with gold outline (like the standings numerals)
  power-ups -> full-colour subject + a Glow Accent (#2979FF) halo outside the rim

    python make_hud_icons.py <out_dir> [size_px]
Writes <name>.svg and <name>.png (transparent) for each icon.
"""

import os
import sys

import cairosvg

BLACK, GOLD, GOLD_DK, WHITE, BLUE, RED = "#141414", "#FFC72C", "#C9961A", "#FFFFFF", "#2979FF", "#FF3131"
SUB = f'stroke="{GOLD}" stroke-width="9" stroke-linejoin="round" paint-order="stroke"'  # white-subject outline
INK = f'stroke="{BLACK}" stroke-width="8" stroke-linejoin="round" paint-order="stroke"'


def frame(inner, glow=False):
    halo = ""
    if glow:
        halo = (f'<circle cx="256" cy="256" r="250" fill="{BLUE}" opacity="0.35"/>'
                f'<circle cx="256" cy="256" r="243" fill="none" stroke="{BLUE}" stroke-width="7"/>')
    return f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" width="512" height="512">
{halo}
<circle cx="256" cy="256" r="234" fill="{BLACK}"/>
<circle cx="256" cy="256" r="222" fill="{GOLD}"/>
<path d="M256 34 A222 222 0 0 1 478 256 L462 256 A206 206 0 0 0 256 50 Z" fill="{GOLD_DK}" opacity="0.55" transform="rotate(90 256 256)"/>
<path d="M70 200 A190 190 0 0 1 200 66" fill="none" stroke="{WHITE}" stroke-width="10" stroke-linecap="round" opacity="0.75"/>
<circle cx="256" cy="256" r="184" fill="{BLACK}"/>
<path d="M118 190 A150 150 0 0 1 220 104" fill="none" stroke="#3a3a3a" stroke-width="14" stroke-linecap="round"/>
<circle cx="80" cy="256" r="7" fill="{WHITE}" opacity="0.8"/>
{inner}
</svg>'''


SQUIRREL = f'''
<path id="tail" d="M232 352 C335 372 398 312 392 232 C386 160 330 124 284 140 C248 153 250 198 286 202" fill="none" stroke="{GOLD}" stroke-width="88" stroke-linecap="round"/>
<path d="M232 352 C335 372 398 312 392 232 C386 160 330 124 284 140 C248 153 250 198 286 202" fill="none" stroke="{WHITE}" stroke-width="70" stroke-linecap="round"/>
<path d="M300 330 C350 315 372 280 368 236" fill="none" stroke="#E8E2D0" stroke-width="14" stroke-linecap="round"/>
<g {SUB} fill="{WHITE}">
  <ellipse cx="214" cy="322" rx="58" ry="62"/>
  <path d="M158 200 L166 148 L194 186 Z"/>
  <path d="M220 184 L248 146 L254 200 Z"/>
  <circle cx="204" cy="232" r="58"/>
</g>
<ellipse cx="206" cy="330" rx="32" ry="40" fill="#F3E2C3"/>
<ellipse cx="182" cy="246" rx="30" ry="22" fill="#F3E2C3"/>
<circle cx="218" cy="222" r="14" fill="{BLACK}"/><circle cx="213" cy="217" r="5" fill="{WHITE}"/>
<circle cx="158" cy="242" r="9" fill="{BLACK}"/>
<path d="M200 198 L234 208" stroke="{BLACK}" stroke-width="7" stroke-linecap="round"/>
<path d="M166 264 Q181 274 196 262" fill="none" stroke="{BLACK}" stroke-width="6" stroke-linecap="round"/>
<rect x="172" y="266" width="12" height="12" rx="2" fill="{WHITE}" stroke="{BLACK}" stroke-width="3"/>
<ellipse cx="222" cy="300" rx="20" ry="16" fill="#C98C55" stroke="{BLACK}" stroke-width="5"/>
'''

PIPE = f'''
<defs><clipPath id="body"><rect x="150" y="196" width="210" height="130" rx="10"/></clipPath></defs>
<g transform="rotate(-18 256 260)">
  <path d="M368 214 L420 214 M378 260 L440 260 M368 306 L420 306" stroke="{WHITE}" stroke-width="12" stroke-linecap="round" opacity="0.8"/>
  <rect x="150" y="196" width="210" height="130" rx="10" fill="{WHITE}" {SUB}/>
  <g clip-path="url(#body)">
    {''.join(f'<polygon points="{x},196 {x+34},196 {x-6},326 {x-40},326" fill="{c}"/>'
             for x, c in [(200, GOLD), (234, BLACK), (268, GOLD), (302, BLACK), (336, GOLD)])}
  </g>
  <ellipse cx="150" cy="261" rx="40" ry="66" fill="{WHITE}" {SUB}/>
  <ellipse cx="150" cy="261" rx="26" ry="48" fill="#2A2E31"/>
</g>
'''

FRISBEE = f'''
<path d="M120 330 Q200 380 300 350 M100 290 Q170 330 250 318" fill="none" stroke="{WHITE}" stroke-width="11" stroke-linecap="round" opacity="0.75"/>
<g transform="rotate(-12 280 240)">
  <ellipse cx="280" cy="250" rx="140" ry="64" fill="{WHITE}" {SUB}/>
  <ellipse cx="280" cy="236" rx="110" ry="42" fill="none" stroke="#E8E2D0" stroke-width="6"/>
  <polygon points="280,200 290,226 318,226 296,242 304,268 280,252 256,268 264,242 242,226 270,226" fill="{BLACK}"/>
</g>
'''

FRESHMAN = f'''
<path d="M226 170 Q226 132 256 132 Q288 132 288 160 Q288 180 262 188 L262 206" fill="none" stroke="{BLACK}" stroke-width="32" stroke-linecap="round"/>
<path d="M226 170 Q226 132 256 132 Q288 132 288 160 Q288 180 262 188 L262 206" fill="none" stroke="{GOLD}" stroke-width="18" stroke-linecap="round"/>
<circle cx="262" cy="232" r="12" fill="{GOLD}" {INK}/>
<rect x="222" y="330" width="22" height="58" rx="6" fill="{BLACK}" stroke="{GOLD}" stroke-width="6"/>
<rect x="268" y="330" width="22" height="58" rx="6" fill="{BLACK}" stroke="{GOLD}" stroke-width="6"/>
<ellipse cx="228" cy="392" rx="24" ry="13" fill="{WHITE}" {SUB}/>
<ellipse cx="286" cy="392" rx="24" ry="13" fill="{WHITE}" {SUB}/>
<g transform="rotate(-6 256 300)">
  <rect x="150" y="250" width="212" height="118" rx="6" fill="{WHITE}" {SUB}/>
  <path d="M203 252 V366 M256 252 V366 M309 252 V366 M152 309 H360" stroke="#C9BE9A" stroke-width="5"/>
  <rect x="166" y="320" width="34" height="26" fill="#27AE60"/>
  <path d="M170 350 L210 300 L246 330 L284 284 L330 300" fill="none" stroke="{RED}" stroke-width="7" stroke-linecap="round" stroke-linejoin="round" stroke-dasharray="14 10"/>
  <circle cx="334" cy="298" r="9" fill="{RED}"/>
</g>
'''

COOKIE = f'''
<circle cx="250" cy="262" r="126" fill="#C98A45" {INK}/>
<circle cx="246" cy="256" r="110" fill="#E4B271"/>
{''.join(f'<polygon points="{x-16},{y-6} {x+2},{y-18} {x+18},{y-2} {x+8},{y+16} {x-12},{y+12}" fill="#4A2C1A" stroke="{BLACK}" stroke-width="4" stroke-linejoin="round"/>'
         for x, y in [(206, 208), (290, 214), (238, 270), (190, 300), (300, 300), (260, 336), (170, 250)])}
<polygon points="352,232 300,330 340,330 314,412 404,300 360,300 392,232" fill="{BLUE}" stroke="{WHITE}" stroke-width="10" stroke-linejoin="round" paint-order="stroke"/>
'''

MEAL_SWIPE = f'''
<g transform="rotate(-12 256 260)">
  <rect x="122" y="168" width="268" height="176" rx="22" fill="{GOLD}" {INK}/>
  <rect x="138" y="184" width="236" height="144" rx="12" fill="{BLACK}"/>
  <rect x="138" y="204" width="236" height="30" fill="{BLUE}"/>
  <polygon points="190,252 200,276 226,276 205,292 213,318 190,302 167,318 175,292 154,276 180,276" fill="{GOLD}"/>
  <text x="190" y="298" font-family="Georgia, serif" font-weight="bold" font-size="30" fill="{BLACK}" text-anchor="middle">V</text>
  <rect x="244" y="262" width="104" height="14" rx="5" fill="{WHITE}"/>
  <rect x="244" y="290" width="72" height="14" rx="5" fill="{WHITE}"/>
</g>
'''

ICONS = {
    "icon-hazard-squirrel": (SQUIRREL, False),
    "icon-hazard-pipe": (PIPE, False),
    "icon-hazard-frisbee": (FRISBEE, False),
    "icon-hazard-freshman": (FRESHMAN, False),
    "icon-powerup-speed-cookie": (COOKIE, True),
    "icon-powerup-meal-swipe": (MEAL_SWIPE, True),
}


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else "icons"
    size = int(sys.argv[2]) if len(sys.argv) > 2 else 512
    os.makedirs(out, exist_ok=True)
    for name, (inner, glow) in ICONS.items():
        svg = frame(inner, glow)
        with open(os.path.join(out, name + ".svg"), "w") as f:
            f.write(svg)
        cairosvg.svg2png(bytestring=svg.encode(), write_to=os.path.join(out, name + ".png"),
                         output_width=size, output_height=size)
        print("[icon]", name)


if __name__ == "__main__":
    main()
