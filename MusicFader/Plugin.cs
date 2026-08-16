using BepInEx;
using FMOD.Studio;
using HarmonyLib;
using SuperFantasyKingdom;

namespace MusicFader
{
	[BepInPlugin("ownly.musicfader", "Music Fader", "1.0.0")]
	public class Plugin : BaseUnityPlugin
	{
		private void Awake()
		{
			new Harmony(Info.Metadata.GUID).PatchAll();
			Logger.LogInfo("Music Fader loaded");
		}
	}

	// ------------------------------ crossfade ------------------------------
	[HarmonyPatch(typeof(MusicManager), "Update")]
	internal static class Patch_Crossfade
	{
		private const float Duration = 5f;
		private const float VanillaDuration = 1.5f;

		private static readonly AccessTools.FieldRef<MusicManager, bool> CityMusic =
			AccessTools.FieldRefAccess<MusicManager, bool>("m_CityMusic");
		private static readonly AccessTools.FieldRef<MusicManager, float> FadeBetween =
			AccessTools.FieldRefAccess<MusicManager, float>("m_MusicFadeBetween");

		private static bool s_WasCity = true;
		private static float s_LastFade;

		private static void Postfix(MusicManager __instance)
		{
			bool city = CityMusic(__instance);
			float fade = FadeBetween(__instance);
			if (city != s_WasCity)
			{
				s_WasCity = city;
				fade -= s_LastFade;
				FadeBetween(__instance) = fade;
			}
			else if (fade < s_LastFade)
			{
				fade = s_LastFade - (s_LastFade - fade) * (VanillaDuration / Duration);
				FadeBetween(__instance) = fade;
			}
			s_LastFade = fade;
		}
	}

	// ------------------------------ combat start ------------------------------

	[HarmonyPatch(typeof(MusicManager), "OnCombatStart")]
	internal static class Patch_StartInTown
	{
		private static readonly AccessTools.FieldRef<MusicManager, bool> CityMusic =
			AccessTools.FieldRefAccess<MusicManager, bool>("m_CityMusic");
		private static readonly AccessTools.FieldRef<MusicManager, float> FadeBetween =
			AccessTools.FieldRefAccess<MusicManager, float>("m_MusicFadeBetween");
		private static readonly AccessTools.FieldRef<MusicManager, float> CombatFadeTimer =
			AccessTools.FieldRefAccess<MusicManager, float>("m_CombatMusicFadeTimer");
		private static readonly AccessTools.FieldRef<MusicManager, EventInstance> CombatMusic =
			AccessTools.FieldRefAccess<MusicManager, EventInstance>("m_CombatMusic");

		private static void Postfix(MusicManager __instance)
		{
			if (!CityMusic(__instance))
			{
				return;
			}
			CombatMusic(__instance).setVolume(0f);
			CombatFadeTimer(__instance) = 0f;
			FadeBetween(__instance) = 0f;
		}
	}
}
