"""Shared palette for every Commodore Circuit interactable.

One 256x256 texture, an 8x8 grid of 32px swatches. Every model maps each part's
UVs onto the centre of one swatch, so all interactables share ONE material
(one draw call batch on Quest) and recolouring is a texture edit, not a remodel.

Slot order is load-bearing: models reference swatches by name, and the grid
position is derived from list order. Append new colours at the end only.
"""

GRID = 8
CELL = 32
SIZE = GRID * CELL  # 256

# (name, hex, emissive?)  — design-doc colours first, then interactable-specific.
PALETTE = [
    ("black",        "#141414", False),
    ("gold",         "#FFC72C", False),
    ("white",        "#FFFFFF", False),
    ("sky_blue",     "#7EC8E3", False),
    ("lawn_green",   "#27AE60", False),
    ("brick_lit",    "#9C4A33", False),
    ("brick_shadow", "#6B2F22", False),
    ("stone_trim",   "#8A7A68", False),
    ("lime",         "#AEEA00", False),
    ("warning_red",  "#FF3131", False),
    ("glow_blue",    "#2979FF", True),   # Glow Accent: the ONLY emissive slot
    ("steel",        "#9AA3AB", False),
    ("steel_dark",   "#5E666D", False),
    ("pipe_inside",  "#2A2E31", False),
    ("gold_dark",    "#C9961A", False),
    ("cookie",       "#E4B271", False),
    ("cookie_edge",  "#C98A45", False),
    ("chip",         "#4A2C1A", False),
    ("fur",          "#A0632F", False),
    ("fur_dark",     "#6B4423", False),
    ("cream",        "#F3E2C3", False),
    ("acorn",        "#C98C55", False),
    ("skin",         "#F2C29B", False),
    ("hair",         "#4A2E1E", False),
    ("denim",        "#2F4A6B", False),
    ("map_paper",    "#F7F1DC", False),
    ("map_line",     "#C9BE9A", False),
    ("pink",         "#F28C8C", False),
]

NAMES = [p[0] for p in PALETTE]


def hex_to_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def slot_uv(name):
    """UV (Blender/Unity convention, origin bottom-left) of a swatch centre."""
    i = NAMES.index(name)
    col, row = i % GRID, i // GRID
    return ((col + 0.5) / GRID, 1.0 - (row + 0.5) / GRID)


def write_textures(base_path, emission_path):
    """Base colour atlas + emission mask (black everywhere except emissive slots)."""
    from PIL import Image
    base = Image.new("RGBA", (SIZE, SIZE), (255, 0, 255, 255))  # magenta = unused slot
    emis = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 255))
    for i, (name, hx, glow) in enumerate(PALETTE):
        col, row = i % GRID, i // GRID
        box = (col * CELL, row * CELL, (col + 1) * CELL, (row + 1) * CELL)
        rgb = hex_to_rgb(hx)
        base.paste(rgb + (255,), box)
        if glow:
            emis.paste(rgb + (255,), box)
    base.save(base_path)
    emis.save(emission_path)
