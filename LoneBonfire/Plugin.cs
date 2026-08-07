using System.Text.RegularExpressions;
using BepInEx;
using HarmonyLib;
using SuperFantasyKingdom;
using SuperFantasyKingdom.Buildings;
using SuperFantasyKingdom.Clickable;
using SuperFantasyKingdom.Spawner;
using UnityEngine;

namespace LoneBonfire
{
	[BepInPlugin("ownly.lonebonfire", "Lone Bonfire", "1.1.0")]
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

		internal static BuildingType Type;
		internal static bool TypeKnown;

		internal static int CountPotentialOutposts(CityManager city)
		{
			int outposts = city.GetOutposts().Count;
			foreach (BuildingSpotWorld spot in Object.FindObjectsOfType<BuildingSpotWorld>())
			{
				if (spot.size == BuildingSize.WorldBig)
				{
					outposts++;
				}
			}
			return outposts;
		}
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

	// the build-menu card is a prefab, no Awake ran on it, so Bonfire.Type is unknown here
	[HarmonyPatch(typeof(Building), "GetDescription")]
	internal static class Patch_BonfireDescription
	{
		private static readonly Regex FirstNumber = new Regex("\\d+");

		private static void Postfix(Building __instance, ref string __result)
		{
			try
			{
				CityManager city = CityManager.Instance;
				if (!(__instance is BuildingOutpostDifficulty) || city == null || string.IsNullOrEmpty(__result))
				{
					return;
				}
				int outposts = Bonfire.CountPotentialOutposts(city);
				string percent = (Mathf.RoundToInt(Bonfire.BonusPerOutpost * 100f) * outposts).ToString();
				__result = FirstNumber.IsMatch(__result)
					? FirstNumber.Replace(__result, percent, 1)
					: __result + " (+" + percent + "%)";
			}
			catch
			{
			}
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
				multiplier = 1f + Bonfire.BonusPerOutpost * Bonfire.CountPotentialOutposts(city);
			}
			Traverse.Create(__instance).Field("m_MonsterAmountMultiplier").SetValue(multiplier);
		}
	}
}
