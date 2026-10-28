# Commodore Circuit — Design Language

## 1. Concept & Style Anchor

*This section comes first because everything below depends on it.*

- **One-paragraph mood/vibe statement:** Commodore Circuit is a cartoonized arcade racer featuring a sensationalized take on Vanderbilt's most iconic campus spots, rendered in bright, high-key daylight. Exaggerated proportions, bold clean shapes, and saturated color do the work. The world should feel like a theme-park version of campus — recognizable enough that Vanderbilt students clock it immediately, exaggerated enough that it reads as a game.

- **AI prompt style suffix** *(paste this onto the end of every image-gen prompt so outputs stay consistent no matter who's generating)*:
  `stylized cartoon illustration, bold clean outlines, flat cel-shading, vibrant saturated colors, exaggerated proportions, high-key daytime lighting with soft minimal shadows, playful collegiate arcade-racer aesthetic, smooth rounded forms, cheerful energetic mood`

- **Reference touchstones:** Mario Kart's bright exaggerated tracks, Overwatch's clean stylized character/prop shading, Fall Guys' cheerful rounded-shape energy.

---

## 2. Logo & Wordmark

![Primary logo](assets/logo-primary.png)

**Primary lockup** (`logo-primary.png`) — the full-detail version: wordmark, star-and-V, circuit-track loop, plus the VR-headset and scooter icons worked into the two track stops. Use for the title screen, sprint video thumbnail, and this document's own header.

| Variant | File | Use |
|---|---|---|
| Full lockup, clean | `logo-full-lockup-clean.png` | Same wordmark + star-and-V + track loop, without the headset/scooter icons — for when the full-detail version is too busy (medium-width banners, loading screens) |
| Wordmark only | `logo-wordmark-only.png` | No icon at all — for narrow horizontal spaces: in-game UI headers, credits, anywhere the full lockup won't fit |
| Icon only | `logo-icon-only.png` | Star-and-V inside the track loop with the motion arrow — for favicon/app-icon-sized spaces |
| All three together | `logo-alternates-sheet.png` | Quick side-by-side reference when deciding which variant fits a given spot |

![Logo alternates](assets/logo-alternates-sheet.png)

> **Loading-screen spinner.** `logo-icon-only.png`. Animate it rotating in Unity.

- **Where it's used:** title screen (primary), loading screen (icon-only, animated), in-game UI headers (wordmark-only), favicon/app icon (icon-only).

---

## 3. Color Palette & Typography

![Color and typography styleguide](assets/color-typography-styleguide.png)

This single styleguide card covers both color and type. The tables below are the same information in text form, plus usage rules that don't fit on the card itself.

**Core brand:**
| Name | Hex |
|---|---|
| Black | `#141414` |
| Bright Gold | `#FFC72C` |
| White | `#FFFFFF` |

> Brighter than Vanderbilt's official muted bronze gold — chosen so it stays legible and pops against the bright green lawn and sky blue in a high-key, stylized VR scene.

**Environment:**
| Name | Hex | Use |
|---|---|---|
| Sky Blue | `#7EC8E3` | Sky/atmosphere |
| Lawn Green | `#27AE60` | Alumni Lawn grass |
| Brick Lit | `#9C4A33` | Campus architecture — sunlit brick faces |
| Brick Shadow | `#6B2F22` | Campus architecture — shadowed/recessed brick |
| Stone Trim | `#8A7A68` | Campus architecture — sandstone window/arch trim |

**Scooter paint options:** Black (`#141414`), White (`#FFFFFF`), Bright Gold (`#FFC72C`), Lime Green (`#AEEA00`).

**Functional accents:**
| Name | Hex | Use |
|---|---|---|
| Warning Red | `#FF3131` | Wrong-direction signage (#16) |
| Glow Accent | `#2979FF` | Obstacles/power-ups (#19) — emissive material + Bloom in Unity |

**Usage hierarchy:** ~60% environment base (Sky Blue + Lawn Green), ~30% architecture (Brick Lit/Shadow + Stone Trim), ~10% high-saturation attention-grabbers (Bright Gold, Warning Red, Glow Accent, scooter paint).

**Typography:**
| Role | Font | Where it's used |
|---|---|---|
| Display / Heading | **Anton Italic** | Title screen, section headers, in-world signage |
| Accent / Flourish | **Luckiest Guy** | Callouts, badges, checkpoint names |
| Body / UI | **Poppins** (SemiBold+) | Menus, settings, player names |
| HUD / Numerals | **Rajdhani** (Bold) | Timer, speed readout, standings |

---

## 4. Graphics & UI — 🟡 In Progress

| # | Asset | Status | File |
|---|---|---|---|
| 1 | Lap counter / timer HUD plate | ✅ Generated (generic) | `hud-empty-plate-frame.png` |
| 2 | Standings badges (1st–4th) | ✅ Generated | `hud-standings-badges.png` |
| 3 | "Wrong Direction" sign (#16) | ✅ Generated | `hud-wrong-way-sign.png` |
| 4 | Finish-line banner | ✅ Generated | `hud-finish-banner.png` |
| 5 | Meal swipe power-up icon | ⬜ Not yet generated | — |
| 6 | Hazard icons (freshman/squirrel/frisbee) | ⬜ Not yet generated | — |
| 7 | Loading-screen spinner | ✅ Already covered — see `logo-icon-only.png` in Section 2 | — |
| 8 | "Let's Race!" button | ✅ Generated (both states) | `ui-lets-race-button.png` |
| 9 | Lobby player-counter panel | ✅ Generated | `ui-lobby-player-counter.png` |
| 10 | Winner / freeze-frame graphic | ✅ Generated | `ui-winner-badge.png` |
| 11 | Speed-boost meter | ⬜ Not yet generated | — |
| 12 | Settings gear icon | ⬜ Not yet generated | — |

**Generated assets:**

![Wrong Way sign](assets/hud-wrong-way-sign.png)

![Finish banner](assets/hud-finish-banner.png)

![Standings badges](assets/hud-standings-badges.png)

![Lap/timer plate](assets/hud-empty-plate-frame.png)

![Let's Race button](assets/ui-lets-race-button.png)

![Lobby player counter](assets/ui-lobby-player-counter.png)

---

## 5. Scooter / Vehicle Direction — ✅ Done

![Scooter reference sheet](assets/scooter-reference-sheet.png)

- **Visual style reference:** Same silhouette as the real Lime scooters around campus, exaggerated and chunkier — flat deck, tall handlebar stem, visible rear motor housing, small star-and-V badge on the stem.
- **Color options:** Black (`#141414`), White (`#FFFFFF`), Bright Gold (`#FFC72C`), Lime Green (`#AEEA00`).

---

## 6. Scenes / Map (Alumni Lawn Environment) — ⬜ TBD
