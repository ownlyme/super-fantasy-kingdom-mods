using BepInEx;
using HarmonyLib;
using SuperFantasyKingdom;

namespace AchievementEnabler
{
	[BepInPlugin("ownly.achievementenabler", "Achievement Enabler", "1.0.0")]
	public class Plugin : BaseUnityPlugin
	{
		private void Awake()
		{
			new Harmony(Info.Metadata.GUID).PatchAll();
			Logger.LogInfo("Achievement Enabler loaded: achievements unlock while modded");
		}
	}

	// game blocks achievements once a bepinex assembly is loaded, clear the flag before each check
	[HarmonyPatch(typeof(AchievementManager), "CheckAchievements")]
	internal static class Patch_EnableAchievements
	{
		private static void Prefix(AchievementManager __instance)
		{
			Traverse.Create(__instance).Field("m_IsModded").SetValue(false);
		}
	}
}
