global using Vector2 = Microsoft.Xna.Framework.Vector2;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Core.Platforms;
using MonoMod.Utils;
using ReLogic.Graphics;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using Terraria.ModLoader.IO;
using Terraria.UI;
using static Terraria.ID.ItemID;

namespace InfoAccessorySlots;
[ReinitializeDuringResizeArrays]
public class InfoAccessorySlots : Mod {
	public static bool[] IsAnInfoAccessory { get; }
	public static bool[] IsAMechanicalAccessory { get; }
	static InfoAccessorySlots() {
		IsAnInfoAccessory = Sets.Factory.CreateNamedSet(nameof(IsAnInfoAccessory))
		.Description($"{nameof(Player)}.{nameof(Player.RefreshInfoAccsFromItemType)} will be called on items in this set when they are in info/passive item slots")
		.RegisterBoolSet();
		IsAMechanicalAccessory = Sets.Factory.CreateNamedSet(nameof(IsAMechanicalAccessory))
		.Description($"{nameof(ItemLoader)}.{nameof(ItemLoader.UpdateInventory)} and {nameof(Player)}.{nameof(Player.RefreshInfoAccsFromItemType)} will be called on items in this set when they are in passive item slots")
		.RegisterBoolSet(
			DontHurtNatureBookInactive,
			DontHurtCrittersBookInactive,
			DontHurtComboBookInactive,
			UncumberingStone
		);
		int itemType = -1;
		static bool matchComparison(Instruction i) {
			if (i.OpCode == OpCodes.Beq) return true;
			if (i.OpCode == OpCodes.Beq_S) return true;
			if (i.OpCode == OpCodes.Bne_Un) return true;
			if (i.OpCode == OpCodes.Bne_Un_S) return true;
			if (i.OpCode == OpCodes.Ceq) return true;
			return false;
		}
		Func<Instruction, bool>[][] matches = [
			[
				i => i.MatchLdarg(1),
				i => i.MatchLdcI4(out itemType),
				matchComparison
			],
			[
				i => i.MatchLdcI4(out itemType),
				i => i.MatchLdarg(1),
				matchComparison
			]
		];
		foreach (Empty _ in ReadMethod(nameof(Player.RefreshInfoAccsFromItemType), matches)) {
			IsAnInfoAccessory[itemType] = true;
		}
		foreach (Empty _ in ReadMethod(nameof(Player.RefreshMechanicalAccsFromItemType), matches)) {
			IsAMechanicalAccessory[itemType] = true;
		}
		for (int i = Count; i < ItemLoader.ItemCount; i++) {
			if (ItemLoader.GetItem(i) is not ModItem modItem) continue;
			if (modItem.GetType().GetMethod(nameof(ModItem.UpdateInfoAccessory)).DeclaringType != typeof(ModItem)) {
				IsAnInfoAccessory[i] = true;
			}
			if (modItem.GetType().GetMethod(nameof(ModItem.UpdateInventory)).DeclaringType != typeof(ModItem)) {
				IsAMechanicalAccessory[i] = true;
			}
		}
	}
	static MethodInfo GetMethod(string name) {
		return typeof(Player).GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, [typeof(int)]);
	}
	static IEnumerable<Empty> ReadMethod(string name, Func<Instruction, bool>[][] matches) => ReadMethod(GetMethod(name), matches);
	static IEnumerable<Empty> ReadMethod(MethodInfo method, Func<Instruction, bool>[][] matches) {
		ILContext context = new(new DynamicMethodDefinition(PlatformTriple.Current.GetIdentifiable(method)).Definition);
		for (int i = 0; i < matches.Length; i++) {
			ILCursor cursor = new(context);
			while (cursor.TryGotoNext(MoveType.After, matches[i])) yield return default;
		}
	}
	public static void MaxMax<T>(ref T current, ref T @new) where T : IComparisonOperators<T, T, bool> {
		if (current < @new) current = @new;
		else if (current > @new) @new = current;
	}
	struct Empty { }
}
public class InfoAccessorySlotsConfig : ModConfig {
	public static InfoAccessorySlotsConfig Instance;
	public override ConfigScope Mode => ConfigScope.ServerSide;
	[DefaultValue(SlotMode.Both)]
	public SlotMode SlotMode = SlotMode.Both;
	[DefaultValue(true)]
	public bool AllowUsable = true;
	[DefaultValue(4), Range(1, int.MaxValue - 1)]
	public int SlotCount = 4;
	[DefaultValue(0)]
	public int SlotOffset = 0;
	public static LocalizedText SlotTypeName => Language.GetText($"Mods.{nameof(InfoAccessorySlots)}.TextOverSlots.{Instance.SlotMode}");
	public static bool IsValidForSlots(Item item) {
		if (item.IsAir) return true;
		if (!Instance.AllowUsable && item.useStyle != ItemUseStyleID.None) return false;
		switch (Instance.SlotMode) {
			case SlotMode.InfoAccessories:
			return InfoAccessorySlots.IsAnInfoAccessory[item.type];

			case SlotMode.MechAccessories:
			return InfoAccessorySlots.IsAMechanicalAccessory[item.type];

			default:
			case SlotMode.Both:
			return InfoAccessorySlots.IsAnInfoAccessory[item.type] || InfoAccessorySlots.IsAMechanicalAccessory[item.type];
		}
	}
}
public enum SlotMode {
	InfoAccessories = 0b01,
	MechAccessories = 0b10,
	Both = 0b11,
}
public class InfoAccessorySlotSystem : ModSystem {
	public override void PostAddRecipes() {
		List<string> infoAccessories = [];
		List<string> mechAccessories = [];
		for (int i = 0; i < InfoAccessorySlots.IsAnInfoAccessory.Length; i++) {
			bool isInfo = InfoAccessorySlots.IsAnInfoAccessory[i];
			bool isMech = InfoAccessorySlots.IsAMechanicalAccessory[i];
			if (!isInfo && !isMech) continue;
			string name = Search.GetName(i);
			if (isInfo) infoAccessories.Add(name);
			if (isMech) mechAccessories.Add(name);
		}
		Mod.Logger.Info($"Info Accessories: {string.Join(", ", infoAccessories)}");
		Mod.Logger.Info($"Mechanical Accessories: {string.Join(", ", mechAccessories)}");
	}
	public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers) {
		DisabledTooltipDisplay.Reset();
		int inventoryIndex = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Inventory"));
		if (inventoryIndex != -1) {
			layers.Insert(inventoryIndex + 1, new LegacyGameInterfaceLayer($"{nameof(InfoAccessorySlots)}: Favorites", () => {
				Draw(Main.spriteBatch);
				return true;
			}, InterfaceScaleType.UI) {
				Active = Main.playerInventory
			});
		}
	}
	static void Draw(SpriteBatch spriteBatch) {
		Main.inventoryScale = 0.6f;
		int x = 534 + 37 * (1 + InfoAccessorySlotsConfig.Instance.SlotOffset);

		Vector2 defaultSize = FontAssets.MouseText.Value.MeasureString("Ammo");
		string displayText = InfoAccessorySlotsConfig.SlotTypeName.Value;
		Vector2 textSize = FontAssets.MouseText.Value.MeasureString(displayText);
		float oversizeFix = defaultSize.X / textSize.X;
		if (oversizeFix > 1) oversizeFix = 1;
		spriteBatch.DrawString(
			FontAssets.MouseText.Value,
			displayText,
			new Vector2(x + 15, 95f),
			Main.MouseTextColorReal,
			0f,
			textSize * 0.5f,
			0.75f * oversizeFix,
			SpriteEffects.None,
		0f);

		float size = TextureAssets.InventoryBack.Width() * Main.inventoryScale;
		Item[] items = Main.LocalPlayer.GetModPlayer<InfoAccessorySlotsPlayer>().items;
		for (int i = 0; i < items.Length; i++) {
			int y = (int)(85f + i * 56 * Main.inventoryScale + 20f);
			bool overMax = i >= InfoAccessorySlotsConfig.Instance.SlotCount;
			bool disabled = !InfoAccessorySlotsConfig.IsValidForSlots(items[i]);
			if (!PlayerInput.IgnoreMouseInterface) {
				if (Main.mouseX >= x && Main.mouseX <= x + size && Main.mouseY >= y && Main.mouseY <= y + size) {
					Main.LocalPlayer.mouseInterface = true;
					DisabledTooltipDisplay.CurrentIsDisabled = disabled;
					DisabledTooltipDisplay.CurrentIsOverMax = overMax;
					ItemSlot.OverrideHover(items, ItemSlot.Context.InventoryAmmo, i);
					if (CanPutItemInSlot(Main.mouseItem, overMax)) {
						ItemSlot.LeftClick(items, ItemSlot.Context.InventoryItem, i);
						ItemSlot.RightClick(items, ItemSlot.Context.InventoryItem, i);
						if (Main.mouseLeftRelease && Main.mouseLeft) Recipe.FindRecipes();
					}
					ItemSlot.MouseHover(items, ItemSlot.Context.InventoryAmmo, i);
				}
			}
			disabled |= overMax;
			ItemSlot.Draw(spriteBatch, items, disabled ? ItemSlot.Context.VoidItem : ItemSlot.Context.InventoryAmmo, i, new Vector2(x, y), disabled ? Color.DarkGray : default);
		}
	}
	static bool CanPutItemInSlot(Item item, bool overMax) {
		if (item.IsAir) return true;
		if (overMax) return false;
		return InfoAccessorySlotsConfig.IsValidForSlots(item);
	}
}
public class InfoAccessorySlotsPlayer : ModPlayer {
	public Item[] items = new Item[4];
	public override void Load() {
		MonoModHooks.Add(typeof(PlayerLoader).GetMethod(nameof(PlayerLoader.ResetInfoAccessories)), (hook_ResetInfoAccessories)ResetInfoAccessories);
	}
	delegate void orig_ResetInfoAccessories(Player self);
	delegate void hook_ResetInfoAccessories(orig_ResetInfoAccessories orig, Player self);
	static void ResetInfoAccessories(orig_ResetInfoAccessories orig, Player self) {
		orig(self);
		if (!Main.gameMenu) self.GetModPlayer<InfoAccessorySlotsPlayer>().RefreshInfoAccessories();
	}
	public override void ResetEffects() {
		if (items.Length < InfoAccessorySlotsConfig.Instance.SlotCount) {
			Array.Resize(ref items, InfoAccessorySlotsConfig.Instance.SlotCount);
		} else if (items.Length > InfoAccessorySlotsConfig.Instance.SlotCount) {
			int minSize = InfoAccessorySlotsConfig.Instance.SlotCount;
			for (int i = 0; i < items.Length; i++) {
				if (items[i]?.IsAir ?? true) {
					for (int j = i + 1; j < items.Length; j++) {
						if (items[j]?.IsAir == false) {
							Utils.Swap(ref items[i], ref items[j]);
							break;
						}
					}
				}
				if (i >= InfoAccessorySlotsConfig.Instance.SlotCount && items[i]?.IsAir == false) minSize = i + 1;
			}
			Array.Resize(ref items, minSize);
		}
	}
	public void RefreshInfoAccessories() {
		for (int i = 0; i < items.Length; i++) {
			items[i] ??= new();
			if (items[i].IsAir || !InfoAccessorySlotsConfig.IsValidForSlots(items[i])) continue;
			bool isMech = InfoAccessorySlots.IsAMechanicalAccessory[items[i].type];// separate if statements to preserve order
			if (isMech) ItemLoader.UpdateInventory(items[i], Main.LocalPlayer);
			Player.RefreshInfoAccsFromItemType(items[i]);
			if (isMech) Player.RefreshMechanicalAccsFromItemType(items[i].type);
		}
	}
	public override IEnumerable<Item> AddMaterialsForCrafting(out ItemConsumedCallback itemConsumedCallback) {
		itemConsumedCallback = null;
		return items;
	}
	public override void SaveData(TagCompound tag) {
		for (int i = 0; i < items.Length; i++) items[i] ??= new();
		tag["Items"] = items.ToList();
	}
	public override void LoadData(TagCompound tag) {
		items = tag.SafeGet<Item[]>("Items", []).ToArray();
	}
}
public class DisabledTooltipDisplay : GlobalItem {
	public static void Reset() {
		CurrentIsDisabled = false;
		CurrentIsOverMax = false;
	}
	public static bool CurrentIsDisabled { get; set; } = false;
	public static bool CurrentIsOverMax { get; set; } = false;
	public override void ModifyTooltips(Item item, List<TooltipLine> tooltips) {
		if (!CurrentIsDisabled && !CurrentIsOverMax) return;
		tooltips.RemoveRange(1, tooltips.Count - 1);
		if (CurrentIsDisabled) tooltips.Add(new(Mod, "DisabledItem", Language.GetTextValue($"Mods.{nameof(InfoAccessorySlots)}.DisabledItem", InfoAccessorySlotsConfig.SlotTypeName.Value)));
		if (CurrentIsOverMax) tooltips.Add(new(Mod, "SlotOverMax", Language.GetTextValue($"Mods.{nameof(InfoAccessorySlots)}.SlotOverMax", InfoAccessorySlotsConfig.SlotTypeName.Value)));
		tooltips.Add(new(Mod, "DisabledForSomeReason", Lang.tip[1].Value));
	}
}
static class Ext {
	public static T SafeGet<T>(this TagCompound self, string key, T fallback = default) {
		try {
			return self.TryGet(key, out T output) ? output : fallback;
		} catch (Exception) {
			return fallback;
		}
	}
}