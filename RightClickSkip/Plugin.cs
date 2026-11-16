using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using BepInEx;
using HarmonyLib;
using PrimeTween;
using SuperFantasyKingdom;
using SuperFantasyKingdom.Tavern;
using SuperFantasyKingdom.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using SplashScreen = UnityEngine.Rendering.SplashScreen;

namespace RightClickSkip
{
	[BepInPlugin("ownly.rightclickskip", "Right Click Skip", "1.2.0")]
	public class Plugin : BaseUnityPlugin
	{
		private const int IntervalMs = 25;
		private const int MaxMs = 20000;
		private const int VK_RBUTTON = 0x02;

		[DllImport("user32.dll")]
		private static extern short GetAsyncKeyState(int vKey);

		// set on the main thread, which cannot run until the splash is over
		private static volatile bool s_Running;

		private void Awake()
		{
			new Harmony(Info.Metadata.GUID).PatchAll();
			Thread pump = new Thread(SplashPump);
			pump.IsBackground = true;
			pump.Start();
		}

		private void Update()
		{
			s_Running = true;
			// overlays turn the Game map off, GameCancel never fires there
			try
			{
				if ((CardPick.Bouncing != null || EnemyPick.Animating)
					&& Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame
					&& (PauseMenuManger.Instance == null || !PauseMenuManger.Instance.InMenu()))
				{
					// enemy first, the completed bounce runs its Callback on the spot
					EnemyPick.Skip();
					CardPick.Skip();
				}
			}
			catch
			{
			}
		}

		// the splash owns the main loop, no Update until it is over. hence a thread and GetAsyncKeyState
		// no other unity api off-thread, Time and Debug throw
		// latched because the engine re-begins the splash after an early stop
		// never read SplashScreen.isFinished here, that getter dies once the engine tears the splash down
		private static void SplashPump()
		{
			Stopwatch clock = Stopwatch.StartNew();
			bool skipRequested = false;
			try
			{
				while (clock.ElapsedMilliseconds < MaxMs && !s_Running)
				{
					if ((GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0)
					{
						skipRequested = true;
					}
					if (skipRequested)
					{
						SplashScreen.Stop(SplashScreen.StopBehavior.StopImmediate);
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

	// ------------------------------ tavern ------------------------------

	// a wait a left click started is left alone, Next would run twice
	[HarmonyPatch(typeof(TavernFeast), "Skip", new[] { typeof(InputAction.CallbackContext) })]
	internal static class Patch_FeastRightClick
	{
		private static bool Prefix(TavernFeast __instance, InputAction.CallbackContext context)
		{
			try
			{
				if (!context.performed || !(context.control?.device is Mouse mouse) || context.control != mouse.rightButton)
				{
					return true;
				}
				if (!__instance.gameObject.activeSelf || !__instance.enabled || PauseMenuManger.Instance.InMenu()
					|| !Traverse.Create(__instance).Field("m_IsDoneFeasting").GetValue<bool>())
				{
					return true;
				}
				__instance.Next();
				return false;
			}
			catch
			{
			}
			return true;
		}
	}

	// this Skip waits for Time.time to move, and Init pauses time
	[HarmonyPatch(typeof(TavernSummary), "Skip", new[] { typeof(InputAction.CallbackContext) })]
	internal static class Patch_SummaryRightClick
	{
		private static bool Prefix(TavernSummary __instance, InputAction.CallbackContext context)
		{
			try
			{
				if (!(context.control?.device is Mouse mouse) || context.control != mouse.rightButton)
				{
					return true;
				}
				__instance.Continue(context);
				return false;
			}
			catch
			{
			}
			return true;
		}
	}

	// ------------------------------ card picks ------------------------------
	// completing the bounce fires the card's own callback now

	internal static class CardPick
	{
		internal static UICard Bouncing;

		internal static void Skip()
		{
			UICard card = Bouncing;
			Bouncing = null;
			if (card != null)
			{
				Tween.CompleteAll(card.transform);
			}
		}
	}

	// absent on 0.7.085
	[HarmonyPatch]
	internal static class Patch_CardPick
	{
		private static MethodBase TargetMethod()
		{
			return AccessTools.Method(typeof(UIOverlayMerchant), "Select", new[] { typeof(UICard) });
		}

		private static bool Prepare()
		{
			return TargetMethod() != null;
		}

		private static void Postfix(UICard selected)
		{
			CardPick.Bouncing = selected;
			if (EnemyPick.Stage == EnemyPick.Step.Open)
			{
				EnemyPick.Stage = EnemyPick.Step.Picked;
			}
		}
	}

	// ------------------------------ day's enemy ------------------------------
	// the pending delays cannot be stopped, the prefixes drop them once Finish ran

	internal static class EnemyPick
	{
		internal enum Step { Idle, Open, Picked, Jumping, Updated, Done }

		internal static readonly MethodInfo UpdateInfos = AccessTools.Method(typeof(UIMerchantMonsterSelection), "UpdateInfos");
		internal static readonly MethodInfo Finish = AccessTools.Method(typeof(UIMerchantMonsterSelection), "Finish");

		internal static UIMerchantMonsterSelection Selection;
		internal static Step Stage;
		internal static bool Queued;

		internal static bool Animating
		{
			get { return Stage == Step.Picked || Stage == Step.Jumping || Stage == Step.Updated; }
		}

		internal static void Skip()
		{
			if (Stage == Step.Picked)
			{
				Queued = true;
				return;
			}
			if (Stage == Step.Jumping)
			{
				UpdateInfos.Invoke(Selection, null);
			}
			if (Stage == Step.Updated)
			{
				Finish.Invoke(Selection, null);
			}
		}
	}

	[HarmonyPatch]
	internal static class Patch_EnemyPick
	{
		private static bool Prepare()
		{
			return EnemyPick.UpdateInfos != null && EnemyPick.Finish != null
				&& AccessTools.Method(typeof(UIMerchantMonsterSelection), "Callback") != null;
		}

		[HarmonyPatch(typeof(UIMerchantMonsterSelection), "Awake")]
		[HarmonyPostfix]
		private static void Reset(UIMerchantMonsterSelection __instance)
		{
			EnemyPick.Selection = __instance;
			EnemyPick.Stage = EnemyPick.Step.Idle;
			EnemyPick.Queued = false;
		}

		[HarmonyPatch(typeof(UIMerchantMonsterSelection), "Reroll")]
		[HarmonyPostfix]
		private static void Opened()
		{
			if (EnemyPick.Stage == EnemyPick.Step.Idle)
			{
				EnemyPick.Stage = EnemyPick.Step.Open;
			}
		}

		[HarmonyPatch(typeof(UIMerchantMonsterSelection), "Callback")]
		[HarmonyPostfix]
		private static void Jumping(UIMerchantMonsterSelection __instance)
		{
			try
			{
				EnemyPick.Selection = __instance;
				EnemyPick.Stage = EnemyPick.Step.Jumping;
				if (EnemyPick.Queued)
				{
					EnemyPick.Queued = false;
					EnemyPick.Skip();
				}
			}
			catch
			{
			}
		}

		[HarmonyPatch(typeof(UIMerchantMonsterSelection), "UpdateInfos")]
		[HarmonyPrefix]
		private static bool Updating()
		{
			if (EnemyPick.Stage == EnemyPick.Step.Done)
			{
				return false;
			}
			EnemyPick.Stage = EnemyPick.Step.Updated;
			return true;
		}

		[HarmonyPatch(typeof(UIMerchantMonsterSelection), "Finish")]
		[HarmonyPrefix]
		private static bool Finishing()
		{
			if (EnemyPick.Stage == EnemyPick.Step.Done)
			{
				return false;
			}
			EnemyPick.Stage = EnemyPick.Step.Done;
			return true;
		}
	}
}
