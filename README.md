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

## Sprint 2 Write-Up

**Sprint dates:** [Oct 1 – Oct 2X, 2026]
**Video:** [Sprint 2 video](LINK)
**Testable APK:** [CommodoreCircuit-Sprint2.apk](LINK) (see [How to Test](#how-to-test))

### Sprint Goals

- Get the scooter to drive well in VR: acceleration, turning, and drifting
- Build a Vanderbilt campus race track
- Add multiplayer: players join one lobby with a code
- Build the classroom lobby where players gather before a race
- Connect the lobby to the race track
- [Anything else from the Sprint 2 backlog]

### What We Completed

#### Scooter Driving (Alex, Joshua)

- **Lemon scooter** model with acceleration, braking, and reverse
- **Turning:** steer with the left stick, or grab the handlebar with both grips and twist it like a real scooter. The scooter leans into turns.
- **Drift boost:** hold the right trigger in a turn to drift, then let go for a speed boost, with visual boost effects
- **Comfort / motion sickness:** the rider's view stays level while the scooter model leans, a comfort vignette narrows the view at speed, and players
  can recenter and adjust rider height
- Controls: A = go, B = brake/reverse, left stick = steer, right trigger = drift, right stick click = recenter. Keyboard WASD + Space also work for
  testing in the Editor.

#### Vanderbilt Campus Track (Alex)

- First version of the campus race track with walls and track surfaces, built from the scooter test track
- [Grass terrain and nature assets (Joshua)]

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

#### Project Setup (Grayson, [others])

- Android/Quest build settings, Meta XR audio settings, and OpenXR configuration
- Reviewed and merged pull requests between team branches
- [Knathifa: ...]
- [Ty: ...]

### Who Did What

| Member             | Sprint 2 Contributions                                                                                                                  |
| ------------------ | --------------------------------------------------------------------------------------------------------------------------------------- |
| Ty Levine          | [ ... ]                                                                                                                                 |
| Joshua Jung        | Networking (Fusion + Voice), lobby codes, classroom lobby, chalkboard/laser menu, Lobby → Campus transition, comfort/controller tuning, |
| grass terrain      |
| Grayson Smith      | Android/XR build configuration, PR reviews and merges, [ ... ]                                                                          |
| Alex Pearle        | Lemon scooter, drift boost, VR handlebar steering, Vanderbilt campus track                                                              |
| Knathifa Cambridge | [ ... ]                                                                                                                                 |

### Challenges and How We Solved Them

- **Motion sickness:** leaning the camera with the scooter made testers queasy. We keep the view level, let only the scooter model lean, and added a
  comfort vignette.
- **Merging scenes across branches:** Unity scene files conflict badly in git. We moved repeatable setup into Editor menu commands
  (`CommodoreCircuit/...`) so a scene can be rebuilt instead of hand-merged.
- **Joining the wrong lobby:** in Fusion's shared mode, a mistyped code quietly _creates_ an empty room. The creator now tags its room, and joining a
  room without that tag returns you to the menu with "No lobby with that code."
- **Testing multiplayer with one headset:** we added a solo test mode so one person can test the whole flow.
- **Disconnected scenes:** [explain that the Lobby → Campus flow is now the main path and older test scenes are being retired]

### How to Test

1. Download the APK: [link]
2. Install it on a Quest with SideQuest, or run `adb install CommodoreCircuit-Sprint2.apk`
3. Open **Commodore Circuit** from Unknown Sources in your Quest library
4. In the classroom, point the right controller at the chalkboard and pull the trigger on **Create Lobby**
5. [Solo build: the race starts after a short countdown. / Multiplayer build: a second headset enters the code shown on the board.]

- **Motion sickness:** leaning the camera with the scooter made testers queasy. We keep the view level, let only the scooter model lean, and added a comfort vignette.
- **Merging scenes across branches:** Unity scene files conflict badly in git. We moved repeatable setup into Editor menu commands (`CommodoreCircuit/...`) so scene can be rebuilt instead of hand-merged.
- **Joining the wrong lobby:** in Fusion's shared mode, a mistyped code quietly _creates_ an empty room. The creator now tags its room, and joining a room without that tag returns you to the menu with "No
  lobby with that code."
- **Testing multiplayer with one headset:** we added a solo test mode so one person can test the whole flow.
- **Disconnected scenes:** [explain that the Lobby → Campus flow is now the main path and older test scenes are being retired]

### How to Test

1. Download the APK: [link]
2. Install it on a Quest with SideQuest, or run `adb install CommodoreCircuit-Sprint2.apk`
3. Open **Commodore Circuit** from Unknown Sources in your Quest library
4. In the classroom, point the right controller at the chalkboard and pull the trigger on **Create Lobby**
5. [Solo build: the race starts after a short countdown. / Multiplayer build: a second headset enters the code shown on the board.]
6. On the campus track, use the controls listed above

### Not Finished / Next Sprint

- **Finish line and laps:** the race has no end yet. Next sprint we'll add a finish line that sends everyone back to the lobby.
- **Seeing other players on the track:** each player rides their own scooter, but other players' avatars don't appear on the campus track yet
- **Clean up old scenes:** remove or archive test scenes so only Lobby and Campus Track are in the build
- [Other backlog items]
