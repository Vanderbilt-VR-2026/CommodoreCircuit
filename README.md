# CommodoreCircuit

**A Mario Kart-style VR racing game for Meta Quest, set on a lore-accurate version of Vanderbilt's campus.**

Players meet in a VR classroom, join a lobby with a code, and race scooters around campus together.

**Latest build: [Sprint3.apk](https://vanderbilt365-my.sharepoint.com/:f:/g/personal/knathifa_l_cambridge_vanderbilt_edu/IgA9IiQ7uj5YSL_Xj5EwE2QCAfGBX40-DTcwQjQOiXzZqeY?e=32jeNv)**

---

## Contents

- [Requirements](#requirements)
- [Play](#play)
- [Controls](#controls)
- [Sprint Builds and Write-ups](#sprint-builds-and-write-ups)
- [Developers/Roles](#developersroles)

---

## Requirements

- A **Meta Quest** headset (Quest 2, 3, or 3S) with **Developer Mode** turned on
- A computer (Mac or Windows) and a **USB-C data cable** (some charge-only cables won't work)
- One of these free install tools:
  - [Meta Quest Developer Hub (MQDH)](https://developers.meta.com/horizon/documentation/unity/ts-mqdh/), recommended
  - [SideQuest](https://sidequestvr.com/setup-howto)
  - `adb` from the Android SDK (included with Unity's Android Build Support)
- An **internet connection** on the headset. Lobbies and races run online through Photon.
- **Two or more headsets** for a multiplayer race. One headset works for exploring the lobby.

---

## Play

1. **Lobby:** you start in a classroom. Point the right controller at the chalkboard and pull the trigger to press buttons.
2. **Create a lobby:** press **Create Lobby**. A 4-character code appears on the board.
3. **Join a lobby:** on another headset, press **Join Lobby** and type the code on the keypad. Everyone in the lobby can see and hear each other.
4. **Start:** once enough players join, a countdown starts and everyone loads into the race together.
5. **Race:** wait for "Get ready" and **3-2-1**, then ride using the [controls](#controls). The HUD shows your time and lap, and a warning appears if you
   go the wrong way.
6. **Finish:** cross the line on your last lap to see the FINISH banner and the standings. Everyone returns to the lobby after the results.

Press the **left menu button (☰)** at any time during a race to pause. Pausing only stops your scooter; other players keep racing.

---

## Controls

### How to Drive

![How to ride the scooter: grab the handlebar, twist to steer, and the button layout](Design/driving-controls.svg)

1. **Grab the handlebar:** hold the **grip** button (middle finger) on **both** controllers. The scooter only responds while you're holding the bars.
2. **Hold the controllers level:** keep them horizontal to the ground, the way you'd hold the handles of a real scooter.
3. **Go:** pull the **left trigger**.
4. **Brake / reverse:** pull the **right trigger** to slow down. Keep holding it after you stop to back up.
5. **Steer:** keep both grips held and **twist** the bars. Push your right hand forward to turn left, or your left hand forward to turn right.
6. **Drift:** while turning, hold **A**. Let go for a speed boost.

If the handlebar ends up in the wrong spot, click either thumbstick to move it back to your hands.

### All Controls

| Input                                  | Action                                                        |
| -------------------------------------- | ------------------------------------------------------------- |
| Middle finger trigger on both hands    | Grab the handlebar                                            |
| Twist the handlebar                    | Steer                                                         |
| Left trigger                           | Go                                                            |
| Right trigger                          | Brake, then reverse                                           |
| A (or X)                               | Drift while turning (let go for a boost)                      |
| Click either thumbstick                | Recenter the handlebar to your hands                          |
| Right stick up / down (while stopped)  | Raise or lower your rider height                              |
| Left menu button (☰)                   | Pause menu (left stick to move, A or right trigger to choose) |
| Right controller + trigger             | Point at and press buttons on the lobby chalkboard            |
| Keyboard W / S / A·D / Space / R / Esc | Editor testing: go / brake / steer / drift / recenter / pause |

---

## Sprint Builds and Write-ups

Each sprint has its own branch with a write-up of what we built, who did what, and the challenges we faced.

| Sprint   | Branch                             | Video                                      | Build                       |
| -------- | ---------------------------------- | ------------------------------------------ | --------------------------- |
| Sprint 1 | [`Sprint-1`](https://github.com/Vanderbilt-VR-2026/CommodoreCircuit/tree/Sprint-1)          | [Sprint 1 video](https://drive.google.com/file/d/1OvjzkMCDrDujInlI412jbCorAFXqfw4g/view?usp=sharing)                     | [Sprint1.apk](https://vanderbilt365-my.sharepoint.com/:u:/g/personal/knathifa_l_cambridge_vanderbilt_edu/IQD3QxGlTYyYSLAkvOZclbNsAQUnzUqWt1OGWfDIDUsNgjI?e=RBmTVM)         |
| Sprint 2 | [`Sprint-2`](https://github.com/Vanderbilt-VR-2026/CommodoreCircuit/tree/Sprint-2)          | [Sprint 2 video](https://drive.google.com/file/d/1zbQ5yVL8Acbf7EDj6khqVg-LF22HKVvM/view?usp=sharing)                     | [Sprint2.apk](https://vanderbilt365-my.sharepoint.com/:u:/g/personal/knathifa_l_cambridge_vanderbilt_edu/IQCcZSu9Oj8_TJ8FN54l0i2mAZUfM2zG1rn8rijY87jUv6s?e=LtUo2T)         |
| Sprint 3 | [`Sprint-3`](https://github.com/Vanderbilt-VR-2026/CommodoreCircuit/tree/Sprint-3)          | [Sprint 3 video](https://vanderbilt365-my.sharepoint.com/:v:/g/personal/knathifa_l_cambridge_vanderbilt_edu/IQDgj_3fCqq5Q7CB8fvSM-qqAWNQNZ-uTNMU4KRqnGU1ta0?nav=eyJyZWZlcnJhbEluZm8iOnsicmVmZXJyYWxBcHAiOiJPbmVEcml2ZUZvckJ1c2luZXNzIiwicmVmZXJyYWxBcHBQbGF0Zm9ybSI6IldlYiIsInJlZmVycmFsTW9kZSI6InZpZXciLCJyZWZlcnJhbFZpZXciOiJNeUZpbGVzTGlua0NvcHkifX0&e=mfuFD2)             | [Sprint3.apk](https://vanderbilt365-my.sharepoint.com/:u:/g/personal/knathifa_l_cambridge_vanderbilt_edu/IQBZSyg7miYFRYarmNh_eKI-ASJ-pj8XCNXZq7jF-ukDbmk?e=dOfA9G)         |

---

## Developers/Roles

| Member             | Major(s)           | Skills                                        | Role                                             |
| ------------------ | ------------------ | --------------------------------------------- | ------------------------------------------------ |
| Ty Levine          | CMA, Econ, Spanish | Storyboarding, Adobe, writing, presentation   | Designing scenes, communications                 |
| Joshua Jung        | Music, CS          | Frontend, Backend, React, Node, Java, JS      | Networking, Backend development, Asset managing  |
| Grayson Smith      | CS, Math           | Frontend, Backend, React, Figma, JS/TS, Java  | Design Director                                  |
| Alex Pearle        | CS, Math           | Unity, Frontend, Backend, Python, C++         | Develop Game assets/physics, Backend development |
| Knathifa Cambridge | CS                 | C++, Python, Storyboarding, Debugging, HCI/UX | GitHub management, Asset development             |

## Intentional Design:
Start Screen/Lobby - We wanted this space to feel completely separate from the environment/design of the game, but still cohesive with the overall campus theme of the game. With this in mind, we decided to design the lobby to look like a FGH classroom to bring some additional Vanderbilt-specific design to the game. We also added some interactive elements to the lobby scene that players can throw around, pick up, etc. to expand some of the fun, game-like qualities of the actual game scene to the lobby.

Campus Setting/Game Environment Design - Vanderbilt's campus is pretty central to our game concept, so we wanted to choose a track location to replicate that a lot of students and players would recognize. This is why we went with a loop around Alumni Lawn/the central Quad of campus. To ensure the track felt accurate to what students walk through and see everyday, we also made sure to spend quite a bit of time accurately mapping and scaling the buildings in the game, and testing different options for graphics to see what felt the most fun and engaging while also maintaining an aspect of realism.

Interactive Element Design - Another key part of our project has been the interactive elements players interact with over the course of the game. These interactive elements both contribute to making the game feel Vandy-specific, and also make the game fun and unique beyond just driving a scooter around a track. We drew on some common "quintessential" Vanderbilt objects/elements (such as Rand cookies, squirrels, meal swipes, construction, the gold and black colors, etc.) to inform what interactive elements ended up in the game and common graphical themes across different elements of the game.

Interaction Design - One significant design choice we made in terms of the player's interaction/controls with the scooter/hand controllers of the Quest is shifting the hand position from the default manner of holding the two sticks to how someone would hold a scooter (tilting the holding position of the controllers horizontally). We wanted the controls and interaction with the scooter itself to feel as intuitive as possible, so we decided to set up the controls this way rather than adapting the hand position to the default controller. 