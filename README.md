# CommodoreCircuit

**A Mario Kart-style VR racing game for Meta Quest, set on a lore-accurate version of Vanderbilt's campus.**

Players meet in a VR classroom, join a lobby with a code, and race scooters around campus together.

> This branch is a snapshot of the project at the end of **Sprint 1**. See the [`main`](https://github.com/Vanderbilt-VR-2026/CommodoreCircuit/tree/main) branch for the latest version and the full sprint history.

---

## Contents

- [Developers/Roles](#developersroles)
- [How to Test](#how-to-test)
- [Sprint 1](#sprint-1)
- [Plan for Sprint 2](#plan-for-sprint-2)

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

## How to Test

1. Download the Sprint 1 APK: [Sprint1.apk](https://vanderbilt365-my.sharepoint.com/:u:/g/personal/knathifa_l_cambridge_vanderbilt_edu/IQD3QxGlTYyYSLAkvOZclbNsAQUnzUqWt1OGWfDIDUsNgjI?e=RBmTVM) (Vanderbilt OneDrive)
2. Install it on a Quest with SideQuest or Meta Quest Developer Hub, or run `adb install Sprint1.apk`
3. Open **Commodore Circuit** from Unknown Sources in your Quest library
4. Ride the scooter prototype around the test track

---

## Sprint 1

[Sprint 1 video](https://drive.google.com/file/d/1OvjzkMCDrDujInlI412jbCorAFXqfw4g/view?usp=sharing) · [Sprint 1 APK](https://vanderbilt365-my.sharepoint.com/:u:/g/personal/knathifa_l_cambridge_vanderbilt_edu/IQD3QxGlTYyYSLAkvOZclbNsAQUnzUqWt1OGWfDIDUsNgjI?e=RBmTVM)

### Sprint 1 Goals

- Set up the Unity project for Meta Quest VR development
- Decide on the game's concept, art style, and team roles
- Build a first drivable scooter prototype in VR
- Create a design language so everyone's assets look consistent

### Sprint 1: What We Completed

#### Project Setup (Joshua, Knathifa, Grayson)

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

### Sprint 1: Who Did What

| Member             | Sprint 1 Contributions                                                                       |
| ------------------ | -------------------------------------------------------------------------------------------- |
| Ty Levine          | Design language document and visual assets (logos, UI/HUD mockups); Sprint 1 video           |
| Joshua Jung        | XR modules and SDK setup; README with roles and project description; reviewed and merged PRs |
| Grayson Smith      | GitHub setup and initialization                                                              |
| Alex Pearle        | Scooter acceleration and drift physics, VR rig, test track and wall bump                     |
| Knathifa Cambridge | GitHub setup and initialization                                                              |

### Sprint 1: Challenges and How We Solved Them

- **Setting up VR for the first time:** getting OpenXR, the XR Interaction Toolkit, and Android build settings to work together took several tries. We
  committed the working setup so everyone starts from the same configuration.
- **Merging Unity projects:** merging branches brought in hundreds of project and settings files at once. We agreed to merge into `main` through pull
  requests and keep scene work on separate branches.
- **Keeping the art consistent:** with several people making assets, styles could drift apart. The design language document (and a shared AI prompt
  style for concept art) gives everyone the same reference.

---

## Plan for Sprint 2

| Member             | Plan                                                                                               |
| ------------------ | -------------------------------------------------------------------------------------------------- |
| Alex Pearle        | Improve scooter driving: turning, handlebar steering, drift boost; start a Vanderbilt campus track |
| Joshua Jung        | Multiplayer networking and a lobby where players gather before a race                              |
| Grayson Smith      | Vanderbilt-specific assets: buildings, outdoor objects, characters                                 |
| Knathifa Cambridge | Vanderbilt-specific assets: buildings, outdoor objects, characters                                 |
| Ty Levine          | Photograph the real Vanderbilt route for track reference; Sprint 2 video                           |
