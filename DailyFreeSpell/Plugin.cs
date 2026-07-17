using BepInEx;
using HarmonyLib;
using SuperFantasyKingdom;

namespace DailyFreeSpell
{
	[BepInPlugin("ownly.dailyfreespell", "Daily Free Spell", "1.0.0")]
	public class Plugin : BaseUnityPlugin
	{
		private void Awake()
		{
			new Harmony(Info.Metadata.GUID).PatchAll();
			// fires at each day start and on load
			DaytimeManager.OnMorningStart += delegate { FreeSpell.available = true; };
			Logger.LogInfo("Daily Free Spell loaded: first spell cast each day costs 0 faith");
		}
	}

	internal static class FreeSpell
	{
		// shared by combat and city spells
		internal static bool available = true;
	}

	[HarmonyPatch(typeof(SpellManager), "GetSpellCost")]
	internal static class Patch_GetSpellCost
	{
		private static bool Prefix(ref int __result)
		{
			if (FreeSpell.available)
			{
				__result = 0;
				return false;
			}
			return true;
		}
	}

	// Cast reads GetSpellCost while still available, so it spends 0 before this clears the flag
	[HarmonyPatch(typeof(SpellManager), "Cast")]
	internal static class Patch_Cast
	{
		private static void Postfix()
		{
			FreeSpell.available = false;
		}
	}
}
