using HarmonyLib;
using SuperFantasyKingdom;
using SuperFantasyKingdom.Tavern;

namespace MoreRelics
{
	// sous chef: upgrade 1 more meat per night
	internal sealed class SousChef : RelicEntry
	{
		internal const int ExtraMeat = 1;

		private static readonly RelicDef Definition = new RelicDef
		{
			id = "ownly_SousChef",
			cloneFrom = new[] { "GreenHouse", "BetterBerries", "Fruitarian", "SpawnTrees" },
			rarity = Rarity.Epic,
			cost = 8,
			title = "Sous Chef",
			description = "The cook prepares one more meat per night."
		};

		public override RelicDef Def
		{
			get { return Definition; }
		}

		// RelicManager is a GameScene object, the tavern only has the save the day just wrote
		internal static bool HeldInTavern()
		{
			GameData data = ((TavernSaveManager.Instance != null) ? TavernSaveManager.Instance.GetGameData() : null);
			return data != null && data.relics != null && data.relics.Contains(Definition.id);
		}
	}

	[HarmonyPatch(typeof(TavernSaveManager), "GetMeatToCook")]
	internal static class Patch_SousChef
	{
		private static void Postfix(ref int __result)
		{
			try
			{
				if (SousChef.HeldInTavern())
				{
					__result += SousChef.ExtraMeat;
				}
			}
			catch (System.Exception e)
			{
				Plugin.Log?.LogError("sous chef failed on meat to cook: " + e);
			}
		}
	}
}
