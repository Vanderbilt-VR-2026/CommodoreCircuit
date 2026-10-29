# CommodoreCircuit

**A Mario Kart-style VR racing game for Meta Quest, set on a lore-accurate version of Vanderbilt's campus.**

Players meet in a VR classroom, join a lobby with a code, and race scooters around campus together.

> This branch is a snapshot of the project at the end of **Sprint 2**. See the [`main`](https://github.com/Vanderbilt-VR-2026/CommodoreCircuit/tree/main) branch for the latest version and the full sprint history.

---

## Contents

- [Developers/Roles](#developersroles)
- [Controls](#controls)
- [How to Test](#how-to-test)
- [Sprint 2](#sprint-2)
- [Plan for Sprint 3](#plan-for-sprint-3)

---

## Developers/Roles

| Member             | Major(s)           | Skills                                        | Role                                             |
| ------------------ | ------------------ | --------------------------------------------- | ------------------------------------------------ |
| Ty Levine          | CMA, Econ, Spanish | Storyboarding, Adobe, writing, presentation   | Designing scenes, communications                 |
| Joshua Jung        | Music, CS          | Frontend, Backend, React, Node, Java, JS      | Networking, Backend development, Asset managing  |
| Grayson Smith      | CS, Math           | Frontend, Backend, React, Figma, JS/TS, Java  | Design Director                                  |
| Alex Pearle        | CS, Math           | Unity, Frontend, Backend, Python, C++         | Develop Game assets/physics, Backend development |
| Knathifa Cambridge | CS                 | C++, Python, Storyboarding, Debugging, HCI/UX | GitHub management, Asset development             |

---

## Controls

| Input                       | Action                                                                  |
| --------------------------- | ----------------------------------------------------------------------- |
| A                           | Go                                                                      |
| B                           | Brake / reverse                                                         |
| Left stick                  | Steer. You can also grab the handlebar with both grips and twist it     |
| Right trigger               | Drift (let go for a boost)                                              |
| Right stick click           | Recenter                                                                |
| Right controller + trigger  | Point at and press buttons on the lobby chalkboard                      |
| Keyboard WASD + Space       | Editor testing (drive / drift)                                          |

---

## How to Test

1. Download the Sprint 2 APK: [Sprint2.apk](https://vanderbilt365-my.sharepoint.com/:u:/g/personal/knathifa_l_cambridge_vanderbilt_edu/IQCcZSu9Oj8_TJ8FN54l0i2mAZUfM2zG1rn8rijY87jUv6s?e=LtUo2T) (Vanderbilt OneDrive)
2. Install it on a Quest with SideQuest or Meta Quest Developer Hub, or run `adb install Sprint2.apk`
3. Open **Commodore Circuit** from Unknown Sources in your Quest library
4. In the classroom, point the right controller at the chalkboard and pull the trigger on **Create Lobby**
5. On a second headset, choose **Join Lobby** and enter the 4-character code shown on the board.
   For solo testing, set Minimum Players to 1 and the race starts after a short countdown.
6. When the countdown ends, everyone loads into the campus track. Ride using the [controls](#controls).

---

## Sprint 2

[Sprint 2 video](https://drive.google.com/file/d/1zbQ5yVL8Acbf7EDj6khqVg-LF22HKVvM/view?usp=sharing) · [Sprint 2 APK](https://vanderbilt365-my.sharepoint.com/:u:/g/personal/knathifa_l_cambridge_vanderbilt_edu/IQCcZSu9Oj8_TJ8FN54l0i2mAZUfM2zG1rn8rijY87jUv6s?e=LtUo2T)

### Sprint 2 Goals

- Get the scooter to drive well in VR: acceleration, turning, and drifting
- Build a Vanderbilt campus race track that looks like the real campus
- Design Vanderbilt-specific assets: buildings, outdoor objects, and characters
- Add multiplayer: players join one lobby with a code
- Build the classroom lobby where players gather before a race
- Connect the lobby to the race track

### Sprint 2: What We Completed

#### Scooter Driving (Alex, Joshua)

- **Lemon scooter** model with acceleration, braking, and reverse
- **Turning:** steer with the left stick, or grab the handlebar with both grips and twist it like a real scooter. The scooter leans into turns.
- **Drift boost:** hold the right trigger in a turn to drift, then let go for a speed boost, with visual boost effects
- **Comfort / motion sickness:** the rider's view stays level while the scooter model leans, a comfort vignette narrows the view at speed, and players
  can recenter and adjust rider height

#### Vanderbilt Campus Track (Alex, Ty)

- First version of the campus race track with walls and track surfaces, built from the scooter test track
- **Reference photos:** Ty photographed the real Vanderbilt route so the track's layout, buildings, and landmarks match campus
- Grass terrain and nature assets along the track (Joshua)

#### Vanderbilt Assets (Knathifa, Grayson)

- **Buildings:** Vanderbilt-specific campus buildings to line the track (Alumni Hall, EBI Tower, McGill)
- **Outdoor objects:** campus props such as lamp posts, trees, trash cans, a Rand cookie, and a frisbee
- **Interactable characters:** squirrels and students that bring the campus to life and that players can interact with on the track

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

### Sprint 2: Who Did What

| Member             | Sprint 2 Contributions                                                                                                                                   |
| ------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Ty Levine          | Edited and assembled the presentation video; photographed the Vanderbilt track route as reference for the Unity campus                                   |
| Joshua Jung        | Networking (Fusion + Voice), lobby codes, classroom lobby, chalkboard/laser menu, Lobby → Campus transition, comfort/controller tuning, grass terrain    |
| Grayson Smith      | Designed Vanderbilt buildings, outdoor objects, and interactable characters (squirrels, students); Android/XR build configuration; PR reviews and merges |
| Alex Pearle        | Lemon scooter, drift boost, VR handlebar steering, Vanderbilt campus track                                                                               |
| Knathifa Cambridge | Designed Vanderbilt buildings, outdoor objects, and interactable characters (squirrels, students)                                                        |

### Sprint 2: Challenges and How We Solved Them

- **Motion sickness:** leaning the camera with the scooter made testers queasy. We keep the view level, let only the scooter model lean, and added a
  comfort vignette.
- **Merging scenes across branches:** Unity scene files conflict badly in git. We moved repeatable setup into Editor menu commands
  (`CommodoreCircuit/...`) so a scene can be rebuilt instead of hand-merged.
- **Joining the wrong lobby:** in Fusion's shared mode, a mistyped code quietly _creates_ an empty room. The creator now tags its room, and joining a
  room without that tag returns you to the menu with "No lobby with that code."
- **Testing multiplayer with one headset:** we added a solo test mode so one person can test the whole flow.
- **Making the track feel like Vanderbilt:** building from memory wasn't accurate enough, so we photographed the real route and designed assets from
  those photos.
- **Disconnected scenes:** our scenes used to be separate test scenes. The Lobby → Campus Track flow is now the main path, and older test scenes
  are being retired.

### Sprint 2: Known Limitations

- The race has no finish line yet, so it doesn't end on its own
- Other players' avatars don't appear on the campus track yet. Each player rides their own scooter.

---

## Plan for Sprint 3

| Member             | Plan                                                                                                       |
| ------------------ | ---------------------------------------------------------------------------------------------------------- |
| Grayson Smith      | Race HUD and rules: race position (1st, 2nd, ...), standings, lap counter, and other race-specific details |
| Alex Pearle        | Scooter collisions (with walls, objects, and other players) and further drift tuning                       |
| Joshua Jung        | Further motion sickness work, and AI bots that fill empty spots so every race is full                      |
| Knathifa Cambridge | More Vanderbilt-specific track details and assets                                                          |
| Ty Levine          | More Vanderbilt-specific track details; Sprint 3 presentation video                                        |
