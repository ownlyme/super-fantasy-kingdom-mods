using BepInEx;
using HarmonyLib;
using SuperFantasyKingdom;
using SuperFantasyKingdom.Buildings;
using SuperFantasyKingdom.Spawner;

namespace LoneBonfire
{
	[BepInPlugin("ownly.lonebonfire", "Lone Bonfire", "1.0.0")]
	public class Plugin : BaseUnityPlugin
	{
		private void Awake()
		{
			new Harmony(Info.Metadata.GUID).PatchAll();
			Logger.LogInfo("Lone Bonfire loaded: bonfire capped at one, +10% monsters per outpost built");
		}
	}

	internal static class Bonfire
	{
		internal const float BonusPerOutpost = 0.1f;

		// serialized on the prefab, so it can only be read off a live instance
		internal static BuildingType Type;
		internal static bool TypeKnown;
	}

	[HarmonyPatch(typeof(BuildingOutpostDifficulty), "Awake")]
	internal static class Patch_LearnBonfireType
	{
		private static void Postfix(BuildingOutpostDifficulty __instance)
		{
			Bonfire.Type = __instance.GetBuildingType();
			Bonfire.TypeKnown = true;
		}
	}

	[HarmonyPatch(typeof(CityManager), "CanBuild")]
	internal static class Patch_CapBonfire
	{
		private static bool Prefix(CityManager __instance, BuildingType type, ref bool __result)
		{
			if (!Bonfire.TypeKnown || type != Bonfire.Type || __instance.GetBuilding(type) == null)
			{
				return true;
			}
			__result = false;
			return false;
		}
	}

	[HarmonyPatch(typeof(MonsterSpawner), "SetMonstersToSpawn")]
	internal static class Patch_ScaleWithOutposts
	{
		private static void Prefix(MonsterSpawner __instance)
		{
			float multiplier = 1f;
			CityManager city = CityManager.Instance;
			if (Bonfire.TypeKnown && city != null && city.GetBuilding(Bonfire.Type) != null)
			{
				multiplier = 1f + Bonfire.BonusPerOutpost * city.GetOutposts().Count;
			}
			Traverse.Create(__instance).Field("m_MonsterAmountMultiplier").SetValue(multiplier);
		}
	}
}
