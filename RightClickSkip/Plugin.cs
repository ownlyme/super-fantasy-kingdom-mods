using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using BepInEx;
using HarmonyLib;
using SuperFantasyKingdom;
using SuperFantasyKingdom.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using SplashScreen = UnityEngine.Rendering.SplashScreen;

namespace RightClickSkip
{
	[BepInPlugin("ownly.rightclickskip", "Right Click Skip", "1.0.0")]
	public class Plugin : BaseUnityPlugin
	{
		private const int IntervalMs = 25;
		private const int MaxMs = 20000;
		private const int StableMs = 2000;
		private const int VK_RBUTTON = 0x02;

		[DllImport("user32.dll")]
		private static extern short GetAsyncKeyState(int vKey);

		private void Awake()
		{
			new Harmony(Info.Metadata.GUID).PatchAll();
			Thread pump = new Thread(SplashPump);
			pump.IsBackground = true;
			pump.Start();
		}

		// the splash owns the main loop, no Update until it is over. hence a thread and GetAsyncKeyState
		// no other unity api off-thread, Time and Debug throw
		// latched because the engine re-begins the splash after an early stop
		private static void SplashPump()
		{
			Stopwatch clock = Stopwatch.StartNew();
			bool skipRequested = false;
			long lastActive = 0;
			try
			{
				while (clock.ElapsedMilliseconds < MaxMs)
				{
					if (!SplashScreen.isFinished)
					{
						lastActive = clock.ElapsedMilliseconds;
						if ((GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0)
						{
							skipRequested = true;
						}
						if (skipRequested)
						{
							SplashScreen.Stop(SplashScreen.StopBehavior.StopImmediate);
						}
					}
					else if (clock.ElapsedMilliseconds - lastActive > StableMs)
					{
						break;
					}
					Thread.Sleep(IntervalMs);
				}
			}
			catch
			{
			}
		}
	}

	// ------------------------------ shared state ------------------------------

	internal static class Skipper
	{
		// vanilla sets m_JustSkipped on every successful Skip branch and never reads it, so: free flag
		internal const float Sentinel = -1f;

		internal static readonly FieldInfo JustSkipped = AccessTools.Field(typeof(GameInputManager), "m_JustSkipped");
		internal static readonly FieldInfo CaptionTimer = AccessTools.Field(typeof(UINewDayCaption), "m_WaitTimer");

		internal static UINewDayCaption Caption;
		internal static bool CaptionArmed;

		internal static bool VanillaSkipped(GameInputManager input)
		{
			return !Sentinel.Equals(JustSkipped.GetValue(input));
		}

		// CaptionArmed is load bearing, see Patch_ArmDayCaption
		internal static bool TrySkipDayCaption()
		{
			if (!CaptionArmed || CaptionTimer == null || Caption == null || !Caption.gameObject.activeSelf)
			{
				return false;
			}
			CaptionTimer.SetValue(Caption, 0.0001f);
			return true;
		}
	}

	// ------------------------------ patches ------------------------------

	// scene reloads daily, so this re-caches and re-disarms per day
	[HarmonyPatch(typeof(UINewDayCaption), "Awake")]
	internal static class Patch_LearnDayCaption
	{
		private static void Postfix(UINewDayCaption __instance)
		{
			Skipper.Caption = __instance;
			Skipper.CaptionArmed = false;
		}
	}

	// the caption's forced pause starts ~1s after it appears, killing it before that freezes the run
	[HarmonyPatch(typeof(UINewDayCaption), "AfterGameStarting")]
	internal static class Patch_ArmDayCaption
	{
		private static void Postfix()
		{
			Skipper.CaptionArmed = true;
		}
	}

	[HarmonyPatch(typeof(GameInputManager), "Skip")]
	internal static class Patch_SkipDayCaption
	{
		private static void Prefix(GameInputManager __instance)
		{
			try
			{
				if (Skipper.JustSkipped != null)
				{
					Skipper.JustSkipped.SetValue(__instance, Skipper.Sentinel);
				}
			}
			catch
			{
			}
		}

		// vanilla has no branch for the caption, so it goes last and a dialogue keeps priority
		private static void Postfix(GameInputManager __instance, InputAction.CallbackContext context)
		{
			try
			{
				if (!context.performed || Skipper.JustSkipped == null || PauseMenuManger.Instance.InMenu())
				{
					return;
				}
				if (Skipper.VanillaSkipped(__instance) || !Skipper.TrySkipDayCaption())
				{
					return;
				}
				Skipper.JustSkipped.SetValue(__instance, Time.time);
			}
			catch
			{
			}
		}
	}

	// right click already arrives as GameCancel, so nothing is bound anywhere
	[HarmonyPatch(typeof(GameInputManager), "GameCancel")]
	internal static class Patch_RightClickSkips
	{
		private static bool Prefix(GameInputManager __instance, InputAction.CallbackContext context)
		{
			try
			{
				if (!context.performed || Skipper.JustSkipped == null || PauseMenuManger.Instance.InMenu())
				{
					return true;
				}
				// gamepad B stays a plain cancel
				if (context.control == null || !(context.control.device is Mouse))
				{
					return true;
				}
				object before = Skipper.JustSkipped.GetValue(__instance);
				__instance.Skip(context);
				if (Skipper.VanillaSkipped(__instance))
				{
					return false;
				}
				Skipper.JustSkipped.SetValue(__instance, before);
			}
			catch
			{
			}
			return true;
		}
	}
}
