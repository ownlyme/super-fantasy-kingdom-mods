using BepInEx;
using HarmonyLib;
using SuperFantasyKingdom;

namespace AchievementEnabler
{
	[BepInPlugin("ownly.achievementenabler", "Achievement Enabler", "1.1.0")]
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
		private static void Prefix(AchievementManager __instance, out bool __state)
		{
			Traverse flag = Traverse.Create(__instance).Field("m_IsModded");
			__state = flag.GetValue<bool>();
			flag.SetValue(false);
		}

		// restored even on a throw, in case a later build reads the flag elsewhere
		private static void Finalizer(AchievementManager __instance, bool __state)
		{
			Traverse.Create(__instance).Field("m_IsModded").SetValue(__state);
		}
	}
}
