using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Winch.Core;
using Winch.Data.Shop;
using Winch.Util;

namespace RodLocker
{
	public class RodLocker : MonoBehaviour
	{
		private const string HarmonyId = "legoless.RodLocker";
		private const string BlackstoneDockId = "dock.outcast-isle";
		private const string RodLockerDestinationId = "destination.rod-locker";

		private static readonly object locker = new object();
		private static readonly FieldInfo destinationButtonPrefabField = AccessTools.Field(typeof(DockUI), "destinationButtonPrefab");
		private static readonly FieldInfo destinationButtonContainerField = AccessTools.Field(typeof(DockUI), "destinationButtonContainer");
		private static readonly FieldInfo destinationButtonObjectsField = AccessTools.Field(typeof(DockUI), "destinationButtonObjects");
		private static readonly FieldInfo destinationButtonsField = AccessTools.Field(typeof(DockUI), "destinationButtons");
		private static readonly HashSet<string> rodLockerItemIds = new HashSet<string>
		{
			"legoless.rodlocker.playstation",
			"legoless.rodlocker.xbox",
			"legoless.rodlocker.switch",
			"legoless.rodlocker.steam",
			"legoless.rodlocker.gog",
			"legoless.rodlocker.ios",
			"legoless.rodlocker.android"
		};
		private static bool harmonyPatched;
		private static bool loggedInjection;
		private static MarketDestination rodLockerDestination;

		public void Awake()
		{
			PatchHarmony();

			if (ApplicationEvents.Instance != null)
			{
				ApplicationEvents.Instance.OnGameLoaded += ResetCachedDestination;
			}

			WinchCore.Log.Debug($"{nameof(RodLocker)} has loaded. Blackstone Rod Locker will be injected into the dock UI without mutating dock data.");
		}

		public void OnDestroy()
		{
			if (ApplicationEvents.Instance != null)
			{
				ApplicationEvents.Instance.OnGameLoaded -= ResetCachedDestination;
			}
		}

		private static void PatchHarmony()
		{
			lock (locker)
			{
				if (harmonyPatched)
				{
					return;
				}

				new Harmony(HarmonyId).PatchAll();
				harmonyPatched = true;
			}
		}

		private static void ResetCachedDestination()
		{
			rodLockerDestination = null;
			loggedInjection = false;
		}

		private static BaseDestination EnsureRodLockerDestination(Dock dock)
		{
			if (dock == null || dock.Data == null || dock.Data.Id != BlackstoneDockId)
			{
				return null;
			}

			if (rodLockerDestination != null)
			{
				return rodLockerDestination;
			}

			try
			{
				var workshop = dock.destinations?.FirstOrDefault(destination =>
					destination != null &&
					(destination.id == "destination.outcast-yard" || destination.gameObject.name == "Workshop"));
				var templateMarket = workshop as MarketDestination;
				var destinationParent = workshop != null ? workshop.transform.parent : dock.transform;

				var lockerObject = new GameObject("Rod Locker Destination");
				lockerObject.transform.SetParent(destinationParent, false);
				lockerObject.transform.localPosition = workshop != null
					? workshop.transform.localPosition + new Vector3(0f, 1.25f, 0f)
					: Vector3.zero;

				var destination = lockerObject.AddComponent<MarketDestination>();
				destination.id = RodLockerDestinationId;
				destination.titleKey = LocalizationUtil.CreateStringsReference("legoless.rodlocker.destination");
				destination.speakerRootNodeOverride = string.Empty;
				destination.alwaysShow = true;
				destination.isIndoors = true;
				destination.icon = TextureUtil.GetSprite("RodIcon");
				destination.loopSFX = null;
				destination.visitSFX = AddressablesUtil.EmptyAssetReference;
				destination.vCam = workshop != null ? workshop.vCam : null;
				destination.highlightConditions = new List<HighlightCondition>();
				destination.selectOnLeft = new List<BaseDestination>();
				destination.selectOnRight = new List<BaseDestination>();
				destination.selectOnUp = new List<BaseDestination>();
				destination.selectOnDown = workshop != null ? new List<BaseDestination> { workshop } : new List<BaseDestination>();
				destination.playerInventoryTabIndexesToShow = templateMarket != null
					? templateMarket.playerInventoryTabIndexesToShow
					: new List<int> { 0, 1, 2 };
				destination.itemTypesBought = ItemType.EQUIPMENT;
				destination.itemSubtypesBought = ItemSubtype.ROD;
				destination.bulkItemTypesBought = ItemType.NONE;
				destination.bulkItemSubtypesBought = ItemSubtype.NONE;
				destination.specificItemsBought = Array.Empty<SpatialItemData>();
				destination.sellValueModifier = 0f;
				destination.allowSellIfGridFull = false;
				destination.allowStorageAccess = true;
				destination.allowRepairs = false;
				destination.allowBulkSell = false;
				destination.bulkSellPromptString = string.Empty;
				destination.bulkSellNotificationString = string.Empty;
				destination.marketTabs = new List<MarketTabConfig>
				{
					new MarketTabConfig
					{
						gridKey = RodLockerEnums.GridKeys.ROD_LOCKER,
						tabSprite = TextureUtil.GetSprite("RodIcon"),
						titleKey = LocalizationUtil.CreateStringsReference("legoless.rodlocker.destination"),
						isUnlockedBasedOnDialogue = false,
						unlockDialogueNodes = new List<string>()
					}
				};

				WinchCore.Log.Debug($"Rod Locker destination anchored to {(workshop != null ? $"{workshop.id}/{workshop.gameObject.name}" : "dock root")}.");
				rodLockerDestination = destination;
				return destination;
			}
			catch (Exception ex)
			{
				WinchCore.Log.Error($"Failed to create Rod Locker destination: {ex}");
				return null;
			}
		}

		private static bool IsRodLockerShop(ShopData shopData)
		{
			return shopData is ModdedShopData moddedShopData && moddedShopData.gridKey == RodLockerEnums.GridKeys.ROD_LOCKER;
		}

		private static bool HasRodLockerItemBeenTaken(string itemId)
		{
			var saveData = GameManager.Instance?.SaveData;
			return saveData?.itemTransactions?.Any(transaction =>
				transaction != null &&
				transaction.itemId == itemId &&
				transaction.bought > 0) ?? false;
		}

		[HarmonyPatch(typeof(ShopData), nameof(ShopData.GetNewStock))]
		private static class ShopDataGetNewStockPatch
		{
			private static void Postfix(ShopData __instance, ref List<SpatialItemData> __result)
			{
				if (!IsRodLockerShop(__instance) || __result == null)
				{
					return;
				}

				__result.RemoveAll(itemData =>
					itemData != null &&
					rodLockerItemIds.Contains(itemData.id) &&
					HasRodLockerItemBeenTaken(itemData.id));
			}
		}

		private static IEnumerator AddRodLockerButtonAfterVanillaUI(DockUI dockUi, Dock dock, IEnumerator original)
		{
			while (true)
			{
				object current;
				try
				{
					if (!original.MoveNext())
					{
						break;
					}

					current = original.Current;
				}
				catch (Exception ex)
				{
					WinchCore.Log.Error($"Vanilla DockUI.ShowUIWithDelay failed before Rod Locker injection: {ex}");
					yield break;
				}

				yield return current;
			}

			(original as IDisposable)?.Dispose();
			AddRodLockerButton(dockUi, dock);
		}

		private static void AddRodLockerButton(DockUI dockUi, Dock dock)
		{
			if (dockUi == null || dock == null || dock.Data == null || dock.Data.Id != BlackstoneDockId)
			{
				return;
			}

			var destination = EnsureRodLockerDestination(dock);
			if (destination == null)
			{
				return;
			}

			try
			{
				var destinationButtons = destinationButtonsField.GetValue(dockUi) as List<DestinationButton>;
				if (destinationButtons != null && destinationButtons.Any(button => button != null && button.destination != null && button.destination.id == RodLockerDestinationId))
				{
					return;
				}

				var prefab = destinationButtonPrefabField.GetValue(dockUi) as GameObject;
				var container = destinationButtonContainerField.GetValue(dockUi) as Transform;
				var destinationButtonObjects = destinationButtonObjectsField.GetValue(dockUi) as List<GameObject>;
				if (prefab == null || container == null)
				{
					WinchCore.Log.Error("Rod Locker could not find the vanilla destination button prefab/container.");
					return;
				}

				var buttonObject = Instantiate(prefab, container);
				var destinationButton = buttonObject.GetComponent<DestinationButton>();
				if (destinationButton == null)
				{
					Destroy(buttonObject);
					WinchCore.Log.Error("Rod Locker destination button prefab did not contain DestinationButton.");
					return;
				}

				destinationButton.Init(destination);
				destinationButtons?.Add(destinationButton);
				destinationButtonObjects?.Add(buttonObject);

				if (!loggedInjection)
				{
					WinchCore.Log.Info($"Rod Locker UI button added after vanilla Blackstone UI. StoredDestinations={dock.destinations?.Count ?? 0}, BoatActionsReady={dock.boatActionsDestination != null}");
					loggedInjection = true;
				}
			}
			catch (Exception ex)
			{
				WinchCore.Log.Error($"Failed to add Rod Locker UI button: {ex}");
			}
		}

		[HarmonyPatch(typeof(DockUI), "ShowUIWithDelay")]
		private static class DockUIShowUIWithDelayPatch
		{
			private static void Postfix(DockUI __instance, Dock dock, ref IEnumerator __result)
			{
				__result = AddRodLockerButtonAfterVanillaUI(__instance, dock, __result);
			}
		}
	}
}
