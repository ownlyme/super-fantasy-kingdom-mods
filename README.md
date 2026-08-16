# Super Fantasy Kingdom mods

Small BepInEx/Harmony mods for Super Fantasy Kingdom

## Mods

**[Bakery Uses Wheat](https://github.com/ownlyme/super-fantasy-kingdom-mods/releases/download/v1.0/BakeryUsesWheat.dll)**  
Bakeries bake bread straight from wheat, skipping the windmill and flour step.

**[Tavern Food Priorities](https://github.com/ownlyme/super-fantasy-kingdom-mods/releases/download/v1.0/TavernFoodPriorities.dll)**  
Units eat the most expendable food first, keeping the cook's meat pipeline flowing.  
With the FishFilet relic: bread, gourmet, berry, cooked, fish, raw  
Without it: bread, gourmet, fish, berry, cooked, raw

**[Achievement Enabler](https://github.com/ownlyme/super-fantasy-kingdom-mods/releases/download/v1.0/AchievementEnabler.dll)**  
Re-enables achievements while BepInEx is installed (including unlocks from challenges on the map!)

**[Extra Starting Carrier](https://github.com/ownlyme/super-fantasy-kingdom-mods/releases/download/v1.0/ExtraStartingCarrier.dll)**  
Start each run with one extra carrier.

**[Daily Free Spell](https://github.com/ownlyme/super-fantasy-kingdom-mods/releases/download/v1.0/DailyFreeSpell.dll)**  
The first spell you cast each day is free, combat or city.

**[Lone Bonfire](https://github.com/ownlyme/super-fantasy-kingdom-mods/releases/download/v1.0/LoneBonfire.dll)**  
You can build only one bonfire, and it scales: +10% monsters for every outpost spot you have revealed, instead of a flat +10%.  

**[Portrait Levels](https://github.com/ownlyme/super-fantasy-kingdom-mods/releases/download/v1.0/PortraitLevels.dll)**  
Displays unit levels on the summary screen and the tavern damage meters.

**[Live Damage Meter](https://github.com/ownlyme/super-fantasy-kingdom-mods/releases/download/v1.0/LiveDamageMeter.dll)**  
Shows the top damage dealers live while you watch the fight, instead of having to wait until entering the tavern.

**[Right Click Skip](https://github.com/ownlyme/super-fantasy-kingdom-mods/releases/download/v1.0/RightClickSkip.dll)**  
Skip anything with right click, including the "New Day" message and the game's splash screens

**[Unlimited Jail](https://github.com/ownlyme/super-fantasy-kingdom-mods/releases/download/v1.0/UnlimitedJail.dll)**  
Jail any amount of units, not just 3  
The first 3 are free, then it costs 1 coin every 2 units

**[Music Fader](https://github.com/ownlyme/super-fantasy-kingdom-mods/releases/download/v1.0/MusicFader.dll)**  
Fades the music between town and battlefield over 5 seconds.

## Install

1. Install BepInEx 5 (Mono) into your Super Fantasy Kingdom folder
2. Drop the .dll files you want into "BepInEx/plugins/"
3. Run the game, the mods load on their own

Tested with the Steam version. The Xbox Game Pass build should work too.

## Build

Needs Super Fantasy Kingdom installed. The .csproj files use paths relative to a BepInEx install ("BepInExCore" and "GameManaged"), so put this repo in "BepInEx/_modsrc", then:

dotnet build -c Release

Move the DLL from "bin/Release/" into "BepInEx/plugins/"

## Support
[https://www.patreon.com/c/OwnlyMods](https://www.patreon.com/c/OwnlyMods)
