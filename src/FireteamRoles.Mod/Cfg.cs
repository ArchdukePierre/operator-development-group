// ODG Operations - FireteamRoles
// Copyright (C) 2026 Archduke, Operator Development Group
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using MelonLoader;
using UnityEngine;

namespace FireteamRoles
{
    internal static class Cfg
    {
        internal static MelonPreferences_Category Category;

        internal static MelonPreferences_Entry<int> LastRole;
        internal static MelonPreferences_Entry<string> RolePanelKey;
        internal static MelonPreferences_Entry<string> GiveKey;
        internal static MelonPreferences_Entry<float> GiveRangeMeters;
        internal static MelonPreferences_Entry<bool> BarAlwaysVisibleInRaid;
        internal static MelonPreferences_Entry<bool> BarCollapsed;
        internal static MelonPreferences_Entry<bool> BarMinimized;
        internal static MelonPreferences_Entry<int> BarAutoCollapseAbove;
        internal static MelonPreferences_Entry<string> BarCollapseKey;
        internal static MelonPreferences_Entry<float> BarWidth;
        internal static MelonPreferences_Entry<float> BarMaxWidth;
        internal static MelonPreferences_Entry<float> BarTabBottomMargin;
        internal static MelonPreferences_Entry<bool> BarBackdrop;
        internal static MelonPreferences_Entry<bool> PickerNative;
        internal static MelonPreferences_Entry<float> BarX;
        internal static MelonPreferences_Entry<float> BarY;
        internal static MelonPreferences_Entry<bool> AllowInPvp;
        internal static MelonPreferences_Entry<bool> AssumeCoop;

        internal static MelonPreferences_Entry<int> BreacherExplosives;
        internal static MelonPreferences_Entry<int> DefaultExplosives;
        internal static MelonPreferences_Entry<int> MedicItemCount;
        internal static MelonPreferences_Entry<int> EpiVials;
        internal static MelonPreferences_Entry<bool> EpiIconTrimBackground;
        internal static MelonPreferences_Entry<bool> MedicAddsMissingItems;
        internal static MelonPreferences_Entry<float> EpiSlotOffsetX;
        internal static MelonPreferences_Entry<float> EpiSlotOffsetY;
        internal static MelonPreferences_Entry<bool> BreacherTopUp;
        internal static MelonPreferences_Entry<float> BreacherTopUpDelay;
        internal static MelonPreferences_Entry<float> BreacherTopUpSpacing;
        internal static MelonPreferences_Entry<string> BreacherLastExplosive;
        internal static MelonPreferences_Entry<string> BreacherTopUpItem;
        internal static MelonPreferences_Entry<float> AssaulterSprintBonus;
        internal static MelonPreferences_Entry<float> SniperHitChanceCut;

        internal static MelonPreferences_Entry<bool> LadderEnabled;
        internal static MelonPreferences_Entry<string> LadderKey;
        internal static MelonPreferences_Entry<float> LadderScale;
        internal static MelonPreferences_Entry<float> LadderRange;
        internal static MelonPreferences_Entry<float> LadderStandoff;
        internal static MelonPreferences_Entry<int> LadderMaxPerPlayer;
        internal static MelonPreferences_Entry<float> LadderMaxSlope;
        internal static MelonPreferences_Entry<float> LadderAssistRadius;
        internal static MelonPreferences_Entry<float> LadderVaultBonus;
        internal static MelonPreferences_Entry<float> LadderVaultReach;
        internal static MelonPreferences_Entry<bool> LadderFallbackClimb;
        internal static MelonPreferences_Entry<float> LadderClimbSpeed;

        internal static MelonPreferences_Entry<bool> BreachEnabled;
        internal static MelonPreferences_Entry<bool> BreachBreacherOnly;
        internal static MelonPreferences_Entry<bool> BreachPreferAuthored;
        internal static MelonPreferences_Entry<float> BreachHoleRadius;
        internal static MelonPreferences_Entry<int> BreachSubdivision;
        internal static MelonPreferences_Entry<int> BreachTriangleBudget;
        internal static MelonPreferences_Entry<int> BreachMaxObjects;
        internal static MelonPreferences_Entry<float> BreachDepth;
        internal static MelonPreferences_Entry<float> BreachHoleTallness;
        internal static MelonPreferences_Entry<float> BreachHoleDrop;
        internal static MelonPreferences_Entry<bool> LadderUseVault;
        internal static MelonPreferences_Entry<float> LadderLength;
        internal static MelonPreferences_Entry<float> LadderTopReach;
        internal static MelonPreferences_Entry<bool> LadderVaultFromGround;
        internal static MelonPreferences_Entry<bool> LadderRaiseVaultLimits;

        internal static MelonPreferences_Entry<int> JtacMissiles;
        internal static MelonPreferences_Entry<bool> JtacRadar;
        internal static MelonPreferences_Entry<float> JtacRadarRange;
        internal static MelonPreferences_Entry<float> JtacRadarSize;
        internal static MelonPreferences_Entry<bool> JtacRadarShowTeam;
        internal static MelonPreferences_Entry<bool> DroneEnabled;
        internal static MelonPreferences_Entry<string> DroneKey;
        internal static MelonPreferences_Entry<int> DroneCount;
        internal static MelonPreferences_Entry<float> DroneBattery;
        internal static MelonPreferences_Entry<float> DroneRange;
        internal static MelonPreferences_Entry<float> DroneSpeed;
        internal static MelonPreferences_Entry<float> DroneClimb;
        internal static MelonPreferences_Entry<float> DroneMouseSens;
        internal static MelonPreferences_Entry<float> DroneFov;
        internal static MelonPreferences_Entry<float> DroneCamTilt;
        internal static MelonPreferences_Entry<bool> DroneThermal;
        internal static MelonPreferences_Entry<bool> DroneThermalWhiteHot;
        internal static MelonPreferences_Entry<bool> DroneDetonate;
        internal static MelonPreferences_Entry<float> DroneBlastRadius;
        internal static MelonPreferences_Entry<float> DroneBlastDamage;
        internal static MelonPreferences_Entry<float> DroneScale;
        internal static MelonPreferences_Entry<float> DroneModelYaw;
        internal static MelonPreferences_Entry<float> DroneCrashSpeed;
        internal static MelonPreferences_Entry<bool> DroneMaskMainCamera;
        internal static MelonPreferences_Entry<int> BreachCutsPerFrame;
        internal static MelonPreferences_Entry<bool> BreachGpuReadback;
        internal static MelonPreferences_Entry<float> BreachWallThickness;
        internal static MelonPreferences_Entry<float> BreachEdgeRoughness;
        internal static MelonPreferences_Entry<int> BreachChunks;
        internal static MelonPreferences_Entry<float> BreachChunkLife;
        internal static MelonPreferences_Entry<float> BreachDemolishMaxSize;
        internal static MelonPreferences_Entry<bool> BreachDebris;
        internal static MelonPreferences_Entry<float> BreachDebrisForce;
        internal static MelonPreferences_Entry<float> BreachHoleWidth;
        internal static MelonPreferences_Entry<float> BreachHoleHeight;
        internal static MelonPreferences_Entry<float> BreachClearDepth;
        internal static MelonPreferences_Entry<float> BreachCornerRadius;
        internal static MelonPreferences_Entry<bool> BreachProveClear;
        internal static MelonPreferences_Entry<int> BreachFrameChunks;
        internal static MelonPreferences_Entry<bool> BreachShowInsides;
        internal static MelonPreferences_Entry<bool> BreachShowRoom;
        internal static MelonPreferences_Entry<float> BreachRoomDepth;
        internal static MelonPreferences_Entry<int> BreachRoomMax;
        internal static MelonPreferences_Entry<bool> LadderAutoLength;
        internal static MelonPreferences_Entry<float> LadderMaxLength;
        internal static MelonPreferences_Entry<float> LadderMinLength;
        internal static MelonPreferences_Entry<float> DroneMaxTilt;
        internal static MelonPreferences_Entry<float> DroneTurboTilt;
        internal static MelonPreferences_Entry<float> DroneYawRate;
        internal static MelonPreferences_Entry<bool> DroneHudHints;
        internal static MelonPreferences_Entry<bool> DronePropsInView;
        internal static MelonPreferences_Entry<float> DroneTurbulence;
        internal static MelonPreferences_Entry<int> DroneFeedHeight;
        internal static MelonPreferences_Entry<float> DroneHudScale;
        internal static MelonPreferences_Entry<float> DroneAccel;
        internal static MelonPreferences_Entry<float> DroneLeanMax;

        internal static MelonPreferences_Entry<bool> DownedEnabled;
        internal static MelonPreferences_Entry<float> DownBleedoutSeconds;
        internal static MelonPreferences_Entry<int> MaxRevivesPerPlayer;
        internal static MelonPreferences_Entry<bool> HeadshotInstantDeath;
        internal static MelonPreferences_Entry<bool> DownedNeedsRescuer;
        internal static MelonPreferences_Entry<float> DownedCrawlSpeed;
        internal static MelonPreferences_Entry<bool> DownedRagdoll;
        internal static MelonPreferences_Entry<float> EpiRequiredBelowHeartRate;
        internal static MelonPreferences_Entry<int> DownedSelfHeals;
        internal static MelonPreferences_Entry<float> DownedSelfHealTimeCost;
        internal static MelonPreferences_Entry<float> DownedSelfHealHPCost;
        internal static MelonPreferences_Entry<int> DownedMinimumWounds;
        internal static MelonPreferences_Entry<float> DownedSeededBleedRate;
        internal static MelonPreferences_Entry<float> TreatmentTimeBonus;
        internal static MelonPreferences_Entry<float> TreatmentTimeBonusCap;
        internal static MelonPreferences_Entry<float> EpiTimeBonus;
        internal static MelonPreferences_Entry<float> EpiHeartRateBoost;
        internal static MelonPreferences_Entry<float> EpiBoostSeconds;
        internal static MelonPreferences_Entry<bool> ReviveRequiresStabilization;
        internal static MelonPreferences_Entry<bool> AutoReviveWhenStabilised;
        internal static MelonPreferences_Entry<float> ReviveHoldSeconds;
        internal static MelonPreferences_Entry<float> TreatRangeMeters;
        internal static MelonPreferences_Entry<float> GrabRangeMeters;
        internal static MelonPreferences_Entry<float> DragSpeed;
        internal static MelonPreferences_Entry<float> DragBreakDistance;
        internal static MelonPreferences_Entry<bool> ReviveFullHealth;
        internal static MelonPreferences_Entry<float> ReviveChestHP;
        internal static MelonPreferences_Entry<float> ReviveHeadHP;
        internal static MelonPreferences_Entry<float> ReviveLimbHP;
        internal static MelonPreferences_Entry<string> GrabKey;
        internal static MelonPreferences_Entry<string> TreatKey;

        internal static MelonPreferences_Entry<bool> TreatShowIcons;
        internal static MelonPreferences_Entry<bool> DebugDumps;
        internal static MelonPreferences_Entry<string> DumpKey;
        internal static MelonPreferences_Entry<int> Revision;

        internal static void Load()
        {
            Category = MelonPreferences.CreateCategory("FireteamRoles");

            LastRole = Category.CreateEntry("LastRole", 0, description: "0 none, 1 assaulter, 2 breacher, 3 sniper, 4 medic");
            RolePanelKey = Category.CreateEntry("RolePanelKey", "F6", description: "toggles the fireteam bar / role picker");
            GiveKey = Category.CreateEntry("GiveKey", "H", description: "medic: hold while looking at a teammate to hand over supplies");
            GiveRangeMeters = Category.CreateEntry("GiveRangeMeters", 3.0f);

            BarAlwaysVisibleInRaid = Category.CreateEntry("BarAlwaysVisibleInRaid", true,
                description: "keep the fireteam bar on screen for the whole raid. Off shows it only with the scoreboard or the picker open");
            BarMinimized = Category.CreateEntry("BarMinimized", false,
                description: "the fireteam window is minimised to a tab along the bottom of the screen. Click the tab, or press the collapse key, to bring it back");
            BarCollapsed = Category.CreateEntry("BarCollapsed", false,
                description: "start the bar collapsed to a single summary line");
            BarAutoCollapseAbove = Category.CreateEntry("BarAutoCollapseAbove", 6,
                description: "collapse automatically once the team is bigger than this, so a full lobby does not take over the screen. 0 never auto-collapses");
            BarCollapseKey = Category.CreateEntry("BarCollapseKey", "F8",
                description: "collapse or expand the fireteam bar");
            BarWidth = Category.CreateEntry("BarWidth", 272f,
                description: "minimum width of the fireteam window; it grows past this on its own when a name needs the room");
            BarMaxWidth = Category.CreateEntry("BarMaxWidth", 420f,
                description: "widest the fireteam window is allowed to grow before names start being shortened instead");
            PickerNative = Category.CreateEntry("PickerNative", true,
                description: "build the role picker out of the game's own UI - its font, its look - instead of the drawn overlay. Falls back to the overlay by itself if it cannot be built");
            BarBackdrop = Category.CreateEntry("BarBackdrop", false,
                description: "put a panel back behind the fireteam readout. Off by default: the readout is drawn as shadowed text on nothing, which stays out of the way until something needs reading");
            BarTabBottomMargin = Category.CreateEntry("BarTabBottomMargin", 6f,
                description: "gap between the minimised tab and the bottom of the screen");
            BarX = Category.CreateEntry("BarX", 12f);
            BarY = Category.CreateEntry("BarY", 120f);
            AllowInPvp = Category.CreateEntry("AllowInPvp", false, description: "roles and downed are PVE only unless this is on");
            AssumeCoop = Category.CreateEntry("AssumeCoop", false,
                description: "skip PVP detection entirely and treat every session as co-op. An escape hatch for when detection misreads a scene; the log line 'pvp=' says what it decided and why");

            BreacherExplosives = Category.CreateEntry("BreacherExplosives", 10);
            DefaultExplosives = Category.CreateEntry("DefaultExplosives", 4, description: "cap enforced for every non-breacher");
            MedicItemCount = Category.CreateEntry("MedicItemCount", 10, description: "medic carries this many of every medical item");
            EpiVials = Category.CreateEntry("EpiVials", 10, description: "epinephrine vials in the medic's kit");
            EpiSlotOffsetX = Category.CreateEntry("EpiSlotOffsetX", 0f,
                description: "nudge the epinephrine slot sideways if it lands on something; positive is right");
            EpiSlotOffsetY = Category.CreateEntry("EpiSlotOffsetY", -46f,
                description: "nudge the epinephrine slot vertically; negative is DOWN, and the default clears the weight bar under the IFAK grid");
            MedicAddsMissingItems = Category.CreateEntry("MedicAddsMissingItems", false,
                description: "also add item types the stock IFAK does not carry; off because the grid has a fixed set of slots");
            BreacherTopUp = Category.CreateEntry("BreacherTopUp", true,
                description: "on raid entry a breacher whose pouch is short is topped up to the cap. This is the perk that actually works: the loadout screen has five separate gates and a save slot between the player and the pouch, the raid spawn has none");
            BreacherTopUpDelay = Category.CreateEntry("BreacherTopUpDelay", 6f,
                description: "seconds after spawning in before the breacher's pouch is topped up, so the game's own loadout spawn finishes first and the two do not both fill it");

            BreacherTopUpItem = Category.CreateEntry("BreacherTopUpItem", "",
                description: "exact name of the explosive to top a breacher up with, eg 'Slap Charge'. Empty copies whatever is already in the pouch. Names are logged on the POUCH line every raid");
            BreacherTopUpSpacing = Category.CreateEntry("BreacherTopUpSpacing", 0.35f,
                description: "seconds between each extra explosive. Each one is a networked spawn request, and firing ten of them inside a single frame is abusive however healthy the rest of it is");
            BreacherLastExplosive = Category.CreateEntry("BreacherLastExplosive", "",
                description: "remembered from the last pouch that had anything in it, and used to top up a breacher who arrives empty. Set by hand if you know the prefab name you want");
            AssaulterSprintBonus = Category.CreateEntry("AssaulterSprintBonus", 0.15f,
                description: "assaulter sprint speed bonus, 0.15 being 15 percent faster");
            SniperHitChanceCut = Category.CreateEntry("SniperHitChanceCut", 0.20f,
                description: "how much of a bot's hit chance is taken away while it is aiming at a sniper, 0.20 being 20 percent less likely to hit");

            LadderEnabled = Category.CreateEntry("LadderEnabled", true);
            LadderKey = Category.CreateEntry("LadderKey", "L",
                description: "assaulter: deploy a ladder against the wall you are looking at");
            LadderScale = Category.CreateEntry("LadderScale", 0.01f,
                description: "the model is authored in centimetres - 321 units tall, 57 wide, 14 deep - so 0.01 gives a 3.2 metre ladder at real-world proportions");
            LadderRange = Category.CreateEntry("LadderRange", 4f,
                description: "how far you can be from a wall and still place a ladder on it");
            LadderStandoff = Category.CreateEntry("LadderStandoff", 0.8f,
                description: "how far the feet sit out from the wall. This is what gives the ladder its lean, and it costs height: a 3.2m ladder standing 0.8m out reaches about 3.1m");
            LadderMaxPerPlayer = Category.CreateEntry("LadderMaxPerPlayer", 1,
                description: "you carry this many. Placing another recovers the one you already put down, so you are never stranded because it is on the far side of the map");
            LadderMaxSlope = Category.CreateEntry("LadderMaxSlope", 40f,
                description: "steepest ground the feet may stand on, in degrees. A ladder on a slope this steep or worse would slide out from under you");
            LadderAssistRadius = Category.CreateEntry("LadderAssistRadius", 1.2f,
                description: "how close to a ladder counts as being on it. Measured to the whole ladder, not just its foot, so it still holds halfway up. About a metre is touching distance for a body");
            LadderVaultBonus = Category.CreateEntry("LadderVaultBonus", 0.6f,
                description: "extra height allowed above the ladder's own length while you are on it");
            LadderVaultReach = Category.CreateEntry("LadderVaultReach", 3f,
                description: "how far ahead the vault system looks for an edge while you are on a ladder. The stock value is tuned for waist-high obstacles and will not find a roof");
            LadderFallbackClimb = Category.CreateEntry("LadderFallbackClimb", true,
                description: "hold forward against a ladder to walk up it. A backstop for when the vault cannot find an edge to grab");
            LadderClimbSpeed = Category.CreateEntry("LadderClimbSpeed", 1.8f);

            BreachEnabled = Category.CreateEntry("BreachEnabled", true,
                description: "cut a real hole in whatever wall a slap charge goes off against, instead of only the handful the game ships an authored destructible on");
            BreachBreacherOnly = Category.CreateEntry("BreachBreacherOnly", true,
                description: "only a breacher opens walls; everyone else's charges still explode and still hurt, they just do not make a doorway");
            BreachPreferAuthored = Category.CreateEntry("BreachPreferAuthored", true,
                description: "where the game already ships a Destructible, let it do the breach. A hand-built result beats a generated one, and it is networked by the game");
            BreachHoleRadius = Category.CreateEntry("BreachHoleRadius", 0.9f,
                description: "radius of the hole in metres; 0.9 is a doorway you can run through, 1.1 takes most of a wall");
            BreachSubdivision = Category.CreateEntry("BreachSubdivision", 4,
                description: "how far triangles around the blast are subdivided before cutting, 0 to 5. Higher is a rounder hole and more vertices");
            BreachTriangleBudget = Category.CreateEntry("BreachTriangleBudget", 250000,
                description: "meshes bigger than this are refused rather than rebuilt, because rebuilding one costs a visible hitch");
            BreachMaxObjects = Category.CreateEntry("BreachMaxObjects", 24,
                description: "how many separate objects one charge may open. A wall is rarely one object - siding, framing, plaster and whatever is stood against the other side are all separate - and a low number here means the charge runs out of budget before it reaches the wall it was stuck to");
            BreachGpuReadback = Category.CreateEntry("BreachGpuReadback", true,
                description: "when a mesh has no CPU copy - which is how a player build ships nearly all geometry, and the ONLY reason a wall refuses to breach - read its vertices back off the graphics card and cut that instead. Turn off if a driver dislikes it");
            BreachCutsPerFrame = Category.CreateEntry("BreachCutsPerFrame", 2,
                description: "how many objects are rebuilt per frame. Every cut rebuilds a whole mesh - one wall in the logs went from 6981 triangles to 15455 - so doing a dozen in a single frame is a visible freeze. Spreading them costs a few frames nobody notices");
            BreachHoleTallness = Category.CreateEntry("BreachHoleTallness", 1.3f,
                description: "how much taller than wide the hole is. 1 is a round hole; 1.45 is a doorway. Stretched along WORLD up, so it stays upright on a tilted or scaled wall");
            BreachHoleDrop = Category.CreateEntry("BreachHoleDrop", 0.3f,
                description: "how far the hole's centre sits below the charge, in metres. A charge is stuck at chest height, and without this the bottom of the hole is a lip you have to step over");
            LadderLength = Category.CreateEntry("LadderLength", 3.21f,
                description: "ladder length in metres, 2.5 to 5.5. 3.21 is the real ladder; longer stretches the model along its rails (rungs further apart) and reaches a taller roof");
            LadderTopReach = Category.CreateEntry("LadderTopReach", 1.1f,
                description: "how far above the ladder's head a lip may be and still be mantled onto from the top rung, in metres. 0 means only edges at or below the head");
            JtacMissiles = Category.CreateEntry("JtacMissiles", 10,
                description: "Hellfires a JTAC Operator gets on the ATAK. Everyone else keeps the ISR's own count. Enforced on the host");
            JtacRadar = Category.CreateEntry("JtacRadar", true,
                description: "the JTAC's radar: a heading-up disc in the bottom left with every live enemy as a red dot");
            JtacRadarRange = Category.CreateEntry("JtacRadarRange", 60f, description: "radar range in metres; enemies beyond it sit dimmed on the rim");
            JtacRadarSize = Category.CreateEntry("JtacRadarSize", 90f, description: "radar radius on screen in pixels");
            JtacRadarShowTeam = Category.CreateEntry("JtacRadarShowTeam", true, description: "teammates as blue dots on the radar");
            DroneEnabled = Category.CreateEntry("DroneEnabled", true);
            DroneKey = Category.CreateEntry("DroneKey", "U",
                description: "throws the JTAC's FPV drone; while it flies, the same key swaps the screen between the drone and your body. BACKSPACE drops it. U because the game binds T (chamber check) and only J L O P U V X are free of the game's own binds");
            DroneCount = Category.CreateEntry("DroneCount", 1, description: "drones a JTAC carries per operation");
            DroneBattery = Category.CreateEntry("DroneBattery", 150f, description: "flight time in seconds");
            DroneRange = Category.CreateEntry("DroneRange", 300f, description: "link range from the pilot in metres; beyond it the feed drops and the drone falls");
            DroneSpeed = Category.CreateEntry("DroneSpeed", 14f, description: "cruise speed at full lean in m/s; the air is tuned so this is where drag balances thrust. SHIFT leans harder and thins the air");
            DroneClimb = Category.CreateEntry("DroneClimb", 5f, description: "climb and descend rate on W and S in m/s. With neither held the drone holds its height");
            DroneMouseSens = Category.CreateEntry("DroneMouseSens", 0.5f, description: "mouse aim sensitivity, as in a shooter: the mouse points the nose, W takes you there");
            DroneFov = Category.CreateEntry("DroneFov", 110f, description: "the goggles' HORIZONTAL field of view in degrees, converted for the screen's aspect; FPV cameras are wide");
            DroneCamTilt = Category.CreateEntry("DroneCamTilt", 4f, description: "camera uptilt from the airframe in degrees. The nose already points where the mouse aims, so only a little is wanted to offset the lean under acceleration");
            DroneThermal = Category.CreateEntry("DroneThermal", false,
                description: "the feed uses the ISR's own FLIR mode while you fly. Needs the ISR in the scene; otherwise the feed is plain video");
            DroneThermalWhiteHot = Category.CreateEntry("DroneThermalWhiteHot", true, description: "true = white hot, false = black hot");
            DroneDetonate = Category.CreateEntry("DroneDetonate", true, description: "click detonates the drone; damage falls off to the blast radius");
            DroneBlastRadius = Category.CreateEntry("DroneBlastRadius", 8f, description: "radius of the warhead's damage in metres; the explosion itself is the game's own frag grenade effect");
            DroneBlastDamage = Category.CreateEntry("DroneBlastDamage", 220f, description: "damage at the centre, falling to about a third at the edge of the radius");
            DroneScale = Category.CreateEntry("DroneScale", 0.15f, description: "model scale; the asset is a 2.3 m airframe, 0.15 makes a hand-launched quad");
            DroneModelYaw = Category.CreateEntry("DroneModelYaw", 180f, description: "yaw of the model on its frame, degrees. Flip by 180 if teammates see it flying backwards");
            DroneCrashSpeed = Category.CreateEntry("DroneCrashSpeed", 6f, description: "an impact that changes the drone's speed by more than this (m/s) destroys it");
            DroneMaskMainCamera = Category.CreateEntry("DroneMaskMainCamera", false,
                description: "while flying, stop the main camera drawing the world to save GPU time. Off by default until the drone view is proven stable");
            DroneMaxTilt = Category.CreateEntry("DroneMaxTilt", 32f,
                description: "how far the airframe leans at full stick, degrees. Speed comes from lean, like a real quad in angle mode");
            DroneTurboTilt = Category.CreateEntry("DroneTurboTilt", 55f,
                description: "lean at full stick with SHIFT held");
            DroneYawRate = Category.CreateEntry("DroneYawRate", 160f,
                description: "yaw rate on A and D in degrees per second; banking adds a coordinated turn on top");
            DroneHudHints = Category.CreateEntry("DroneHudHints", true,
                description: "the one-line control reminder at the bottom of the goggles");
            DronePropsInView = Category.CreateEntry("DronePropsInView", true,
                description: "the blur of the front props in the bottom corners of the feed, as on a real FPV camera");
            DroneTurbulence = Category.CreateEntry("DroneTurbulence", 1f,
                description: "how much the airframe fidgets in the air, 0 to 3");
            DroneAccel = Category.CreateEntry("DroneAccel", 12f,
                description: "how hard the drone accelerates and brakes, m/s^2. Let go of the keys and it brakes to a hover at this rate; SHIFT adds 70%");
            DroneLeanMax = Category.CreateEntry("DroneLeanMax", 14f,
                description: "how far the airframe, and so the picture, leans into acceleration and banks into turns, degrees. 0 for a perfectly steady camera");
            DroneHudScale = Category.CreateEntry("DroneHudScale", 1f,
                description: "size of the goggle readouts; 1 follows the screen's rows (1080 = design size)");
            DroneFeedHeight = Category.CreateEntry("DroneFeedHeight", 720,
                description: "height in pixels of the texture the drone renders into; the width follows the screen. A feed is not a monitor, and 720 keeps the second camera cheap");
            LadderRaiseVaultLimits = Category.CreateEntry("LadderRaiseVaultLimits", false,
                description: "raise the game's own vault height and detection range while stood at a ladder, so the vault key can take the wall directly. Off: the climb handles the top itself, and a raised vault could grab a body sprinting into the ladder");
            LadderVaultFromGround = Category.CreateEntry("LadderVaultFromGround", false,
                description: "hand the whole climb to the game's vault animation from the foot of the ladder instead of climbing the rungs and mantling at the top");
            LadderUseVault = Category.CreateEntry("LadderUseVault", false,
                description: "climb the ladder with the game's own ClimbUp vault - animated, IK'd and networked - when there is a roof to land on. Off, or when there is nothing on top, the rung-by-rung climb is used");
            BreachDepth = Category.CreateEntry("BreachDepth", 1.6f,
                description: "how far past the surface the charge keeps working, in metres. 1.6 takes out the inner skin of a wall and whatever is stood against it; 4 reaches the far side of a room, which is more than a charge should");
            BreachWallThickness = Category.CreateEntry("BreachWallThickness", 0.22f,
                description: "how deep the hole is lined with geometry, in metres. A wall is one sheet of triangles with nothing behind it, so without this you see straight through culled backfaces and it reads as paper");
            BreachEdgeRoughness = Category.CreateEntry("BreachEdgeRoughness", 0.22f,
                description: "how far the rim wanders from a circle, 0 to 0.9. A perfect circle is the giveaway that a hole was computed rather than blown");
            BreachChunks = Category.CreateEntry("BreachChunks", 14,
                description: "lumps of wall thrown out by the blast that settle as rubble. 0 turns them off");
            BreachChunkLife = Category.CreateEntry("BreachChunkLife", 45f,
                description: "seconds before rubble is cleaned up");
            BreachDemolishMaxSize = Category.CreateEntry("BreachDemolishMaxSize", 3f,
                description: "a prop this size or smaller that cannot be cut is destroyed outright instead. Fences, hedges and railings ship with their mesh data stripped so no hole can be carved out of them, and blowing one apart is closer to what a charge would do anyway. Larger objects are never destroyed - a house is not a prop");
            BreachDebris = Category.CreateEntry("BreachDebris", true);
            BreachDebrisForce = Category.CreateEntry("BreachDebrisForce", 14f);
            BreachHoleWidth = Category.CreateEntry("BreachHoleWidth", 1.2f,
                description: "width of the opening in metres. The opening is a doorway measured against the real wall: its two faces, the floor and the ceiling");
            BreachHoleHeight = Category.CreateEntry("BreachHoleHeight", 2.1f,
                description: "height of the opening from the floor in metres, clipped to the ceiling");
            BreachClearDepth = Category.CreateEntry("BreachClearDepth", 1.8f,
                description: "how far past the back of the wall props are blown into the room, in metres. The shelves and crates that used to stop a breach are in this zone");
            BreachCornerRadius = Category.CreateEntry("BreachCornerRadius", 0.28f,
                description: "rounding of the opening's corners in metres");
            BreachProveClear = Category.CreateEntry("BreachProveClear", true,
                description: "after the cut, fire a grid of rays through the opening and clear whatever still blocks one: cut it, frame it, blow it in, or as a last resort switch its collider off when the wall is visibly open. The verdict is in the log");
            BreachShowRoom = Category.CreateEntry("BreachShowRoom", true,
                description: "after a breach, give every object in the room behind it a runtime copy so the level's baked occlusion cannot keep hiding it. That bake decides what is visible from where, it was computed before the hole existed, and without this you look through a fresh hole at the skybox. Per object on purpose: turning the camera's occlusion culling off instead put the whole level's interiors in memory and hung the game");
            BreachRoomDepth = Category.CreateEntry("BreachRoomDepth", 8f,
                description: "how far into the room behind the opening that pass reaches, metres");
            BreachRoomMax = Category.CreateEntry("BreachRoomMax", 90,
                description: "most objects one breach will do that to");
            BreachShowInsides = Category.CreateEntry("BreachShowInsides", true,
                description: "after a breach, draw the inside of any one-skin wall the opening looks onto, so you see into the house rather than through it");
            LadderAutoLength = Category.CreateEntry("LadderAutoLength", true,
                description: "the ladder runs out to stand a step above whatever it leans on, up to LadderMaxLength, and refuses walls it cannot reach. Off: it is always LadderLength long");
            LadderMaxLength = Category.CreateEntry("LadderMaxLength", 6f,
                description: "the longest the ladder extends to, metres");
            LadderMinLength = Category.CreateEntry("LadderMinLength", 2.4f,
                description: "the shortest it is placed at, metres");
            BreachFrameChunks = Category.CreateEntry("BreachFrameChunks", 18,
                description: "static lumps half-sunk round the rim so the edge reads as broken rather than cut. 0 turns them off");

            EpiIconTrimBackground = Category.CreateEntry("EpiIconTrimBackground", true,
                description: "erase the flat background from a dropped-in epi photo, keeping the vial and its white label");

            DownedEnabled = Category.CreateEntry("DownedEnabled", true);
            DownBleedoutSeconds = Category.CreateEntry("DownBleedoutSeconds", 120f);
            MaxRevivesPerPlayer = Category.CreateEntry("MaxRevivesPerPlayer", 1, description: "revives per player per operation");
            HeadshotInstantDeath = Category.CreateEntry("HeadshotInstantDeath", false, description: "true means head kills skip the downed state");
            DownedRagdoll = Category.CreateEntry("DownedRagdoll", false,
                description: "let the body go limp instead of forcing a prone crawl; off, because a casualty who can drag themselves to cover beats a rag doll that cannot");
            DownedCrawlSpeed = Category.CreateEntry("DownedCrawlSpeed", 0.7f,
                description: "how fast a downed player can crawl; 0 pins them in place");

            EpiRequiredBelowHeartRate = Category.CreateEntry("EpiRequiredBelowHeartRate", 0f,
                description: "pulse under which a casualty must be given epinephrine before reviving; 0 disables it. Must be below 30 to ever matter, because a downed pulse sits at 30");
            DownedNeedsRescuer = Category.CreateEntry("DownedNeedsRescuer", true,
                description: "only go down when someone is actually able to reach you; solo and last-man-standing deaths stay instant");
            DownedSelfHeals = Category.CreateEntry("DownedSelfHeals", 1,
                description: "medical items a downed player may use on themselves before the menu locks; a rescuer is still needed for the rest");
            DownedSelfHealTimeCost = Category.CreateEntry("DownedSelfHealTimeCost", 10f,
                description: "seconds of bleedout time a downed self-treatment burns; working on yourself while dying is not free");
            DownedMinimumWounds = Category.CreateEntry("DownedMinimumWounds", 2,
                description: "a casualty always goes down with at least this many open wounds, so there is always a job to do before the revive unlocks");
            DownedSeededBleedRate = Category.CreateEntry("DownedSeededBleedRate", 4f,
                description: "bleed rate given to a seeded wound; higher reads as arterial to the rescuer's suggestion hint");
            TreatmentTimeBonus = Category.CreateEntry("TreatmentTimeBonus", 8f,
                description: "seconds added to the bleedout clock each time a rescuer closes one of the casualty's wounds");
            TreatmentTimeBonusCap = Category.CreateEntry("TreatmentTimeBonusCap", 40f,
                description: "most seconds treatment can add to one down; work buys time, it does not make a casualty immortal");
            EpiHeartRateBoost = Category.CreateEntry("EpiHeartRateBoost", 45f,
                description: "how far epinephrine lifts the pulse; it can only ever raise it, never lower it");
            EpiBoostSeconds = Category.CreateEntry("EpiBoostSeconds", 60f,
                description: "how long the epinephrine lift takes to fade back to the pulse the game itself wants");
            EpiTimeBonus = Category.CreateEntry("EpiTimeBonus", 15f,
                description: "seconds a rescuer's epinephrine vial adds to a casualty's bleedout clock");
            DownedSelfHealHPCost = Category.CreateEntry("DownedSelfHealHPCost", 12f,
                description: "health a downed self-treatment costs, taken off the chest; you buy the patch-up with what is left of you");
            AutoReviveWhenStabilised = Category.CreateEntry("AutoReviveWhenStabilised", true,
                description: "a casualty gets up as soon as their wounds are closed; no extra key to hold once the work is done");
            ReviveRequiresStabilization = Category.CreateEntry("ReviveRequiresStabilization", true, description: "every bleed stopped and every fracture splinted before a revive is allowed");
            ReviveHoldSeconds = Category.CreateEntry("ReviveHoldSeconds", 4f);
            TreatRangeMeters = Category.CreateEntry("TreatRangeMeters", 2.5f);
            GrabRangeMeters = Category.CreateEntry("GrabRangeMeters", 2.5f);
            DragSpeed = Category.CreateEntry("DragSpeed", 1.2f);
            DragBreakDistance = Category.CreateEntry("DragBreakDistance", 3.5f);

            ReviveFullHealth = Category.CreateEntry("ReviveFullHealth", true,
                description: "a casualty whose wounds were treated gets up at full health, using the game's own starting values rather than a fixed number");
            ReviveChestHP = Category.CreateEntry("ReviveChestHP", 35f);
            ReviveHeadHP = Category.CreateEntry("ReviveHeadHP", 15f);
            ReviveLimbHP = Category.CreateEntry("ReviveLimbHP", 20f);
            GrabKey = Category.CreateEntry("GrabKey", "F", description: "grab a downed teammate by the shoulder, press again to release");
            TreatKey = Category.CreateEntry("TreatKey", "E", description: "open a downed teammate's health menu");

            TreatShowIcons = Category.CreateEntry("TreatShowIcons", true,
                description: "picture tiles on the treat screen; turn off to fall back to text tiles if the icons ever misbehave");
            DebugDumps = Category.CreateEntry("DebugDumps", false);
            DumpKey = Category.CreateEntry("DumpKey", "F7", description: "log-only inventory dump of players, wounds and medical state");

            Revision = Category.CreateEntry("Revision", 0,
                description: "config revision, used to apply one-time migrations. Do not edit");

            int rev = Revision.Value;
            if (rev < 1)
            {
                BarAlwaysVisibleInRaid.Value = true;
                MelonLoader.MelonLogger.Msg("FireteamRoles: the fireteam bar is now on for the whole raid (press " +
                                            BarCollapseKey.Value + " to collapse it)");
            }
            if (rev < 2)
            {

                DumpKey.Value = "None";
                MelonLoader.MelonLogger.Msg("FireteamRoles: the F7 inventory dump is unbound. " +
                                            "Set DumpKey to a key name to bring it back.");
            }

            if (rev < 3)
            {

                if (Mathf.Approximately(LadderAssistRadius.Value, 2.5f)) LadderAssistRadius.Value = 1.2f;
            }

            if (rev < 4)
            {

                if (Mathf.Approximately(LadderMaxSlope.Value, 30f)) LadderMaxSlope.Value = 40f;
            }

            if (rev < 6)
            {

                if (DroneKey.Value == "T") DroneKey.Value = "U";
            }

            if (rev < 5)
            {

                if (Mathf.Approximately(BreachHoleRadius.Value, 1.1f)) BreachHoleRadius.Value = 0.9f;
                if (Mathf.Approximately(BreachHoleTallness.Value, 1.45f)) BreachHoleTallness.Value = 1.3f;
                if (Mathf.Approximately(BreachHoleDrop.Value, 0.35f)) BreachHoleDrop.Value = 0.3f;
                if (Mathf.Approximately(BreachDepth.Value, 4.0f)) BreachDepth.Value = 1.6f;
                if (BreachMaxObjects.Value == 16) BreachMaxObjects.Value = 10;
                if (Mathf.Approximately(BreachDemolishMaxSize.Value, 7f)) BreachDemolishMaxSize.Value = 3f;
            }

            if (rev < 7)
            {

                if (BreachTriangleBudget.Value == 60000) BreachTriangleBudget.Value = 250000;
                if (BreachMaxObjects.Value == 10) BreachMaxObjects.Value = 24;
                if (Mathf.Approximately(DroneSpeed.Value, 9f)) DroneSpeed.Value = 14f;
                if (Mathf.Approximately(DroneClimb.Value, 4.5f)) DroneClimb.Value = 5f;
                if (Mathf.Approximately(DroneMouseSens.Value, 0.25f)) DroneMouseSens.Value = 0.5f;
                if (Mathf.Approximately(DroneFov.Value, 95f)) DroneFov.Value = 110f;
                if (Mathf.Approximately(DroneCamTilt.Value, 12f)) DroneCamTilt.Value = 20f;
            }

            if (rev < 8)
            {

                if (DroneThermal.Value) DroneThermal.Value = false;
                if (Mathf.Approximately(DroneCamTilt.Value, 20f)) DroneCamTilt.Value = 4f;
                if (Mathf.Approximately(DroneBlastRadius.Value, 5f)) DroneBlastRadius.Value = 8f;
                if (Mathf.Approximately(DroneBlastDamage.Value, 140f)) DroneBlastDamage.Value = 220f;
            }

            if (rev < 9)
            {

                if (LadderUseVault.Value) LadderUseVault.Value = false;
            }

            if (rev < Rev)
            {
                Revision.Value = Rev;
                try { MelonPreferences.Save(); } catch { }
            }

            Validate();
        }

        const int Rev = 9;

        static void Validate()
        {
            Clamp(DownBleedoutSeconds, 10f, 900f, "DownBleedoutSeconds");
            Clamp(DownedCrawlSpeed, 0f, 6f, "DownedCrawlSpeed");
            Clamp(DragSpeed, 0.1f, 8f, "DragSpeed");
            Clamp(DragBreakDistance, 1.5f, 20f, "DragBreakDistance");
            Clamp(TreatRangeMeters, 0.5f, 15f, "TreatRangeMeters");
            Clamp(GrabRangeMeters, 0.5f, 15f, "GrabRangeMeters");
            Clamp(GiveRangeMeters, 0.5f, 15f, "GiveRangeMeters");
            Clamp(ReviveHoldSeconds, 0.2f, 30f, "ReviveHoldSeconds");
            Clamp(TreatmentTimeBonus, 0f, 120f, "TreatmentTimeBonus");
            Clamp(TreatmentTimeBonusCap, 0f, 600f, "TreatmentTimeBonusCap");
            Clamp(EpiTimeBonus, 0f, 120f, "EpiTimeBonus");
            Clamp(DownedSelfHealTimeCost, 0f, 120f, "DownedSelfHealTimeCost");
            Clamp(DownedSelfHealHPCost, 0f, 200f, "DownedSelfHealHPCost");
            Clamp(DownedSeededBleedRate, 0f, 40f, "DownedSeededBleedRate");
            Clamp(EpiRequiredBelowHeartRate, 0f, 200f, "EpiRequiredBelowHeartRate");
            Clamp(ReviveChestHP, 1f, 500f, "ReviveChestHP");
            Clamp(ReviveHeadHP, 2f, 500f, "ReviveHeadHP");
            Clamp(ReviveLimbHP, 1f, 500f, "ReviveLimbHP");

            ClampInt(MedicItemCount, 1, 99, "MedicItemCount");
            ClampInt(EpiVials, 0, 99, "EpiVials");
            ClampInt(BreacherExplosives, 1, 32, "BreacherExplosives");
            ClampInt(DefaultExplosives, 1, 32, "DefaultExplosives");
            ClampInt(MaxRevivesPerPlayer, 0, 9, "MaxRevivesPerPlayer");
            ClampInt(DownedSelfHeals, 0, 9, "DownedSelfHeals");
            ClampInt(DownedMinimumWounds, 0, 11, "DownedMinimumWounds");
            ClampInt(LastRole, 0, RoleInfo.Count - 1, "LastRole");

            CheckKey(RolePanelKey, "F6"); CheckKey(GiveKey, "H"); CheckKey(BarCollapseKey, "F8");
            CheckKey(LadderKey, "L");
            CheckKey(DroneKey, "U");
            CheckKey(GrabKey, "F");       CheckKey(TreatKey, "E");
            CheckKey(DumpKey, "F7");
        }

        static void Clamp(MelonPreferences_Entry<float> e, float lo, float hi, string name)
        {
            if (e == null) return;
            float v = e.Value;
            if (v >= lo && v <= hi && !float.IsNaN(v)) return;
            float c = float.IsNaN(v) ? lo : Mathf.Clamp(v, lo, hi);
            Log.Warn("config: " + name + " was " + v + ", clamped to " + c + " (valid " + lo + ".." + hi + ")");
            e.Value = c;
        }

        static void ClampInt(MelonPreferences_Entry<int> e, int lo, int hi, string name)
        {
            if (e == null) return;
            int v = e.Value;
            if (v >= lo && v <= hi) return;
            int c = Mathf.Clamp(v, lo, hi);
            Log.Warn("config: " + name + " was " + v + ", clamped to " + c + " (valid " + lo + ".." + hi + ")");
            e.Value = c;
        }

        static void CheckKey(MelonPreferences_Entry<string> e, string fallback)
        {
            if (e == null) return;
            try { Enum.Parse(typeof(KeyCode), e.Value, true); }
            catch
            {
                Log.Warn("config: '" + e.Value + "' is not a key name, falling back to " + fallback +
                         " (names are Unity KeyCodes, e.g. F6, Keypad0, LeftAlt)");
                e.Value = fallback;
            }
        }

        internal static KeyCode Key(MelonPreferences_Entry<string> e, KeyCode fallback)
        {
            try
            {
                if (e == null || string.IsNullOrEmpty(e.Value)) return fallback;
                return (KeyCode)Enum.Parse(typeof(KeyCode), e.Value, true);
            }
            catch { return fallback; }
        }
    }
}
