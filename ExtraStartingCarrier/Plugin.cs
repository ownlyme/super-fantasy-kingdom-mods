using BepInEx;
using HarmonyLib;
using SuperFantasyKingdom;

namespace ExtraStartingCarrier
{
	[BepInPlugin("ownly.extrastartingcarrier", "Extra Starting Carrier", "1.0.0")]
	public class Plugin : BaseUnityPlugin
	{
		private void Awake()
		{
			new Harmony(Info.Metadata.GUID).PatchAll();
			Logger.LogInfo("Extra Starting Carrier loaded: +1 carrier at new-game start");
		}
	}

	// runs once per new game (not on save/load), adds one carrier on top of the race's base
	[HarmonyPatch(typeof(ResourceManager), "GiveStartingResources")]
	internal static class Patch_ExtraCarrier
	{
		private static void Postfix(ResourceManager __instance)
		{
			__instance.AddResource(ResourceType.Carrier, 1);
		}
	}
}
