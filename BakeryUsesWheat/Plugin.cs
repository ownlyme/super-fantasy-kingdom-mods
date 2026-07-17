using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using SuperFantasyKingdom;
using SuperFantasyKingdom.Buildings;

namespace BakeryUsesWheat
{
	[BepInPlugin("ownly.bakeryuseswheat", "Bakery Uses Wheat", "1.1.1")]
	public class Plugin : BaseUnityPlugin
	{
		private void Awake()
		{
			new Harmony(Info.Metadata.GUID).PatchAll();
			Logger.LogInfo("Bakery Uses Wheat loaded: bakery bakes bread straight from wheat");
		}
	}

	// swap flour->wheat before Init reads the recipe and bakes the storage slots in
	[HarmonyPatch(typeof(BuildingResource), "Init")]
	internal static class Patch_Bakery
	{
		private static void Prefix(BuildingResource __instance)
		{
			if (__instance.GetBuildingType() != BuildingType.Bakery)
				return;

			if (__instance is BuildingCrafting crafting)
			{
				List<CraftingRecipe> recipes = crafting.GetCraftingRecipes();
				if (recipes != null)
				{
					for (int i = 0; i < recipes.Count; i++)
					{
						ResourceAmount[] ingredients = recipes[i].ingredients;
						if (ingredients == null)
							continue;
						for (int j = 0; j < ingredients.Length; j++)
						{
							if (ingredients[j].type == ResourceType.Flour)
								ingredients[j].type = ResourceType.Wheat;
						}
					}
				}
			}

			ResourceStorage store = __instance.GetStorage();
			if (store != null)
			{
				List<ResourceAmount> slots = Traverse.Create(store).Field("storage").GetValue<List<ResourceAmount>>();
				if (slots != null)
				{
					for (int i = 0; i < slots.Count; i++)
					{
						if (slots[i].type == ResourceType.Flour)
							slots[i] = new ResourceAmount(ResourceType.Wheat, slots[i].amount);
					}
				}
			}
		}
	}
}
