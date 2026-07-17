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
You can build only one bonfire, and it scales: +10% monsters for every outpost you built, instead of a flat +10%.  

## Install

1. Install BepInEx 5 (Mono) into your Super Fantasy Kingdom folder
2. Drop the .dll files you want into "BepInEx/plugins/"
3. Run the game, the mods load on their own

Tested with the Xbox Game Pass version. Steam should work too.

## Build

Needs Super Fantasy Kingdom installed. Open each .csproj and set the two paths near the top ("BepInExCore" and "GameManaged") to your install, then:

dotnet build -c Release

Move the DLL from "bin/Release/" into "BepInEx/plugins/"

## Support
[https://www.patreon.com/c/OwnlyMods](https://www.patreon.com/c/OwnlyMods)
