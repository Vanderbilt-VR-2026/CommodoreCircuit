# CommodoreCircuit

**The objective of this project is to create a Mario-Kart based racing game that is lore-accurate to Vanderbilt's unique environment on VR**

---

## Developers/Roles

| Member             | Major(s)           | Skills                                        | Role                                             |
| ------------------ | ------------------ | --------------------------------------------- | ------------------------------------------------ |
| Ty Levine          | CMA, Econ, Spanish | Storyboarding, adobe, writing, presentation   | Designing scenes, communications                 |
| Joshua Jung        | Music, CS          | Frontend, Backend, React, Node, Java, JS      | Networking, Backend development, Asset managing  |
| Grayson Smith      | CS, Math           | Frontend, Backend, React, Figma, JS/TS, Java  | Design Director                                  |
| Alex Pearle        | CS, Math           | Unity, Frontend, Backend, Python, C++         | Develop Game assets/physics, Backend development |
| Knathifa Cambridge | CS                 | C++, Python, Storyboarding, Debugging, HCI/UX | Github Management, Asset development             |

[Sprint 1 video](https://drive.google.com/file/d/1OvjzkMCDrDujInlI412jbCorAFXqfw4g/view?usp=sharing)

[Sprint 2 video](https://drive.google.com/file/d/1zbQ5yVL8Acbf7EDj6khqVg-LF22HKVvM/view?usp=sharing)

### Sprint Goals

- Set up the Unity project for Meta Quest VR development
- Decide on the game's concept, art style, and team roles
- Build a first drivable scooter prototype in VR
- Create a design language so everyone's assets look consistent

### What We Completed

#### Project Setup (Joshua, [Knathifa])

- Created the Unity project and GitHub repository with a branch per team member
- Added the XR modules and SDKs needed for Quest: OpenXR, XR Interaction Toolkit, and Android build support
- Wrote the README with the project goal, team members, skills, and roles

#### Scooter Prototype (Alex)

- First version of the scooter with **acceleration and drift physics**
- A VR rig so the player rides the scooter in first person
- A **test track** with walls, plus a "wall bump" that pushes the scooter back when it hits a wall

#### Design Language (Ty, Grayson, Knathifa)

- **Concept and style:** a cartoon arcade racer, a "theme-park version" of Vanderbilt's campus in bright daylight, inspired by Mario Kart, Overwatch,
  and Fall Guys
- **Logo and wordmark:** a primary logo, a clean version, wordmark-only and icon-only variants
- **Color palette and typography:** Vanderbilt black and a brighter gold that stands out in VR, plus environment colors (sky, lawn, campus brick, stone
  trim)
- **UI and HUD mockups:** a "Let's Race" button, lobby player counter, standings badges, finish banner, wrong-way sign, and winner badge
- **Scooter reference sheet** for the scooter model
- Everything is documented in [`Design/DESIGN_LANGUAGE_1.md`](Design/DESIGN_LANGUAGE_1.md)

#### Presentation (Ty)

- Edited and put together the Sprint 1 presentation video

### Who Did What

| Member             | Sprint 1 Contributions                                                                       |
| ------------------ | -------------------------------------------------------------------------------------------- |
| Ty Levine          | Design language document and visual assets (logos, UI/HUD mockups); Sprint 1 video           |
| Joshua Jung        | XR modules and SDK setup; README with roles and project description; reviewed and merged PRs |
| Grayson Smith      | [Design direction for the design language / ...]                                             |
| Alex Pearle        | Scooter acceleration and drift physics, VR rig, test track and wall bump                     |
| Knathifa Cambridge | [GitHub setup and initial Unity project / design assets / ...]                               |

### Challenges and How We Solved Them

- **Setting up VR for the first time:** getting OpenXR, the XR Interaction Toolkit, and Android build settings to work together took several tries. We
  committed the working setup so everyone starts from the same configuration.
- **Merging Unity projects:** merging branches brought in hundreds of project and settings files at once. We agreed to merge into `main` through pull
  requests and keep scene work on separate branches.
- **Keeping the art consistent:** with several people making assets, styles could drift apart. The design language document (and a shared AI prompt
  style for concept art) gives everyone the same reference.

### Not Finished / Next Sprint

| Member             | Plan                                                                                               |
| ------------------ | -------------------------------------------------------------------------------------------------- |
| Alex Pearle        | Improve scooter driving: turning, handlebar steering, drift boost; start a Vanderbilt campus track |
| Joshua Jung        | Multiplayer networking and a lobby where players gather before a race                              |
| Grayson Smith      | Vanderbilt-specific assets: buildings, outdoor objects, characters                                 |
| Knathifa Cambridge | Vanderbilt-specific assets: buildings, outdoor objects, characters                                 |
| Ty Levine          | Photograph the real Vanderbilt route for track reference; Sprint 2 video                           |

### Sprint Goals

- Get the scooter to drive well in VR: acceleration, turning, and drifting
- Build a Vanderbilt campus race track that looks like the real campus
- Design Vanderbilt-specific assets: buildings, outdoor objects, and characters
- Add multiplayer: players join one lobby with a code
- Build the classroom lobby where players gather before a race
- Connect the lobby to the race track

### What We Completed

#### Scooter Driving (Alex, Joshua)

- **Lemon scooter** model with acceleration, braking, and reverse
- **Turning:** steer with the left stick, or grab the handlebar with both grips and twist it like a real scooter. The scooter leans into turns.
- **Drift boost:** hold the right trigger in a turn to drift, then let go for a speed boost, with visual boost effects
- **Comfort / motion sickness:** the rider's view stays level while the scooter model leans, a comfort vignette narrows the view at speed, and players
  can recenter and adjust rider height
- Controls: A = go, B = brake/reverse, left stick = steer, right trigger = drift, right stick click = recenter. Keyboard WASD + Space also work for
  testing in the Editor.

#### Vanderbilt Campus Track (Alex, Ty)

- First version of the campus race track with walls and track surfaces, built from the scooter test track
- **Reference photos:** Ty photographed the real Vanderbilt route so the track's layout, buildings, and landmarks match campus
- Grass terrain and nature assets along the track (Joshua)

#### Vanderbilt Assets (Knathifa, Grayson)

- **Buildings:** Vanderbilt-specific campus buildings to line the track [list which ones, e.g. Kirkland Hall, ...]
- **Outdoor objects:** campus props such as [benches, lamp posts, trees, signs, ...]
- **Interactable characters:** squirrels and students that bring the campus to life and that players can interact with on the track
- [Note which assets are already in Unity and which are still being imported]

#### Multiplayer Networking (Joshua)

- Added **Photon Fusion** (shared mode) and **Photon Voice**, so players in the same lobby can talk
- **Lobby codes:** one player creates a lobby and gets a 4-character code; others type it in to join. Codes leave out I, L, O, 0, and 1 so they're easy
  to read aloud.
- Clear error messages for a wrong code, a full lobby, or a race that already started
- Each player's head and hands are synced as a networked avatar
- A shared countdown starts the race when enough players have joined (shorter if the lobby fills up)

#### Classroom Lobby (Joshua)

- A classroom where players wait before the race, with objects you can pick up and hand presence
- **Chalkboard menu:** Create Lobby / Join Lobby, a keypad for codes, the lobby code, a player count, and the countdown, drawn in a chalk-style font
- **Laser pointer UI:** point the right controller at the board and pull the trigger to press buttons (like the Quest dashboard). Buttons turn gold
  when pointed at. Pressing them with a finger also works.

#### Scene Transitions (Joshua)

- **Lobby → Campus Track:** when the countdown ends, everyone in the lobby loads the campus track together
- If players leave mid-race and only one is left, the room returns to the lobby
- **Solo test mode:** set Minimum Players to 1 to test the full Lobby → Race flow with one headset

#### Presentation & Project Setup (Ty, Grayson)

- **Sprint video:** Ty edited and put together the Sprint 2 presentation video
- Android/Quest build settings, Meta XR audio settings, and OpenXR configuration (Grayson)
- Reviewed and merged pull requests between team branches (Grayson)

### Who Did What

| Member             | Sprint 2 Contributions                                                                                                                  |
| ------------------ | --------------------------------------------------------------------------------------------------------------------------------------- |
| Ty Levine          | Edited and assembled the presentation video; photographed the Vanderbilt track route as reference for the Unity campus                  |
| Joshua Jung        | Networking (Fusion + Voice), lobby codes, classroom lobby, chalkboard/laser menu, Lobby → Campus transition, comfort/controller tuning, |
| grass terrain      |
| Grayson Smith      | Designed Vanderbilt buildings, outdoor objects, and interactable characters (squirrels, students); Android/XR build configuration; PR   |
| reviews and merges |
| Alex Pearle        | Lemon scooter, drift boost, VR handlebar steering, Vanderbilt campus track                                                              |
| Knathifa Cambridge | Designed Vanderbilt buildings, outdoor objects, and interactable characters (squirrels, students)                                       |

### Challenges and How We Solved Them

- **Motion sickness:** leaning the camera with the scooter made testers queasy. We keep the view level, let only the scooter model lean, and added a
  comfort vignette. This is still being tuned (see next sprint).
- **Merging scenes across branches:** Unity scene files conflict badly in git. We moved repeatable setup into Editor menu commands
  (`CommodoreCircuit/...`) so a scene can be rebuilt instead of hand-merged.
- **Joining the wrong lobby:** in Fusion's shared mode, a mistyped code quietly _creates_ an empty room. The creator now tags its room, and joining a
  room without that tag returns you to the menu with "No lobby with that code."
- **Testing multiplayer with one headset:** we added a solo test mode so one person can test the whole flow.
- **Making the track feel like Vanderbilt:** building from memory wasn't accurate enough, so we photographed the real route and designed assets from
  those photos.
- **Disconnected scenes:** last sprint our scenes were separate test scenes. The Lobby → Campus Track flow is now the main path, and older test scenes
  are being retired.

### How to Test

1. Download the APK: [link]
2. Install it on a Quest with SideQuest, or run `adb install CommodoreCircuit-Sprint2.apk`
3. Open **Commodore Circuit** from Unknown Sources in your Quest library
4. In the classroom, point the right controller at the chalkboard and pull the trigger on **Create Lobby**
5. [Solo build: the race starts after a short countdown. / Multiplayer build: a second headset enters the code shown on the board.]
6. On the campus track, use the controls listed above

### Known Limitations

- The race has no finish line yet, so it doesn't end on its own
- Other players' avatars don't appear on the campus track yet. Each player rides their own scooter.
- [Assets not yet imported into the track]

### Next Sprint Plan

| Member             | Plan                                                                                                       |
| ------------------ | ---------------------------------------------------------------------------------------------------------- |
| Grayson Smith      | Race HUD and rules: race position (1st, 2nd, ...), standings, lap counter, and other race-specific details |
| Alex Pearle        | Scooter collisions (with walls, objects, and other players) and further drift tuning                       |
| Joshua Jung        | Further motion sickness work, and AI bots that fill empty spots so every race is full                      |
| Knathifa Cambridge | More Vanderbilt-specific track details and assets                                                          |
| Ty Levine          | More Vanderbilt-specific track details; Sprint 3 presentation video                                        |
