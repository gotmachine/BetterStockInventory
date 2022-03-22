using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using KSP.Localization;
using KSP.UI.Screens;
using UnityEngine;
using static InventoryAPI.Utils;

namespace BetterStockInventory
{
    [HarmonyPatch]
    class UnlimitedInventorySlots
    {
        public const int MAX_STACK_SIZE = 10;

        public static Dictionary<Part, int> prefabsStackableQuantities = new Dictionary<Part, int>();

        public static void OnPartLoaderLoaded()
        {
            prefabsStackableQuantities.Clear();
            foreach (AvailablePart availablePart in PartLoader.Instance.loadedParts)
            {
                foreach (PartModule module in availablePart.partPrefab.Modules)
                {
                    if (module is ModuleCargoPart cargoModule)
                    {
                        prefabsStackableQuantities.Add(availablePart.partPrefab, cargoModule.stackableQuantity);
                        break;
                    }
                }
            }
        }

        #region Unlimited slots

        // Make sure inventory parts can't be stacked
        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIPartActionInventory), "InitializeSlots")]
        static void UIPartActionInventory_InitializeSlots_Prefix(UIPartActionInventory __instance, out UnlimitedInventorySlotsUI __state)
        {
            __state = __instance.gameObject.AddComponent<UnlimitedInventorySlotsUI>();
            __state.PreInitializeSlots(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIPartActionInventory), "InitializeSlots")]
        static void UIPartActionInventory_InitializeSlots_Postfix(UnlimitedInventorySlotsUI __state)
        {
            if (__state != null)
                __state.PostInitializeSlots();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIPartActionInventory), "OnModuleInventoryChanged")]
        static void UIPartActionInventory_OnModuleInventoryChanged_Postfix(UIPartActionInventory __instance, ModuleInventoryPart changedModuleInventoryPart)
        {
            UnlimitedInventorySlotsUI.OnInventoryChanged(__instance, changedModuleInventoryPart);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ModuleInventoryPart), nameof(ModuleInventoryPart.TotalEmptySlots))]
        static bool ModuleInventoryPart_TotalEmptySlots_Prefix(ref int __result)
        {
            __result = int.MaxValue;
            return false;
        }
        #endregion

        #region Unlimited stacking

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ModuleInventoryPart), nameof(ModuleInventoryPart.GetInfo))]
        static bool ModuleInventoryPart_GetInfo_Prefix(ModuleInventoryPart __instance, ref string __result)
        {
            StringBuilder sb = StringBuilderCache.Acquire();
            if (__instance.HasPackedVolumeLimit)
            {
                sb.Append(Localizer.Format("#autoLOC_8003415")); // #autoLOC_8003415 = Volume Limit
                sb.Append(": ");
                sb.Append(__instance.packedVolumeLimit.ToString("0.0L"));
            }
            if (__instance.HasMassLimit)
            {
                if (sb.Length > 0)
                    sb.Append("\n");

                sb.Append(Localizer.Format("#autoLOC_8003416")); // #autoLOC_8003416 = Mass Limit
                sb.Append(": ");
                sb.Append(__instance.massLimit.ToString("0.000 t"));
            }

            __result = sb.ToStringAndRelease();
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ModuleCargoPart), nameof(ModuleCargoPart.OnLoad))]
        static bool ModuleCargoPart_OnLoad_Prefix(ModuleCargoPart __instance)
        {
            UpdateStackableQuantity(__instance);
            return false;
        }

        public static void UpdateStackableQuantity(ModuleCargoPart moduleCargoPart)
        {
            int configStackableQuantity;

            if (moduleCargoPart.part.partInfo == null)
            {
                configStackableQuantity = moduleCargoPart.stackableQuantity;
            }
            else if (!prefabsStackableQuantities.TryGetValue(moduleCargoPart.part.partInfo.partPrefab, out configStackableQuantity))
            {
                moduleCargoPart.stackableQuantity = 0;
                return;
            }

            if (configStackableQuantity > 0)
            {
                if (moduleCargoPart.part.Resources.Count > 0)
                    moduleCargoPart.stackableQuantity = 0;
                else
                    moduleCargoPart.stackableQuantity = MAX_STACK_SIZE;
            }
            else
            {
                moduleCargoPart.stackableQuantity = 0;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ModuleInventoryPart), nameof(ModuleInventoryPart.HasStackingSpace))]
        static bool ModuleInventoryPart_HasStackingSpace_Prefix(ModuleInventoryPart __instance, int slotIndex, ref bool __result)
        {
            if (!__instance.storedParts.TryGetValue(slotIndex, out StoredPart storedPart) || !IsStackable(storedPart))
                __result = false;
            else
                __result = storedPart.quantity < storedPart.stackCapacity;

            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ModuleInventoryPart), nameof(ModuleInventoryPart.GetStackAmountAtSlot))]
        static bool ModuleInventoryPart_GetStackAmountAtSlot_Prefix(ModuleInventoryPart __instance, int slotIndex, ref int __result)
        {
            if (__instance.storedParts.TryGetValue(slotIndex, out StoredPart storedPart))
                __result = storedPart.quantity;
            else
                __result = 0;

            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ModuleInventoryPart), nameof(ModuleInventoryPart.GetStackCapacityAtSlot))]
        static bool ModuleInventoryPart_GetStackCapacityAtSlot_Prefix(ModuleInventoryPart __instance, int slotIndex, ref int __result)
        {
            if (!__instance.storedParts.TryGetValue(slotIndex, out StoredPart storedPart) || !IsStackable(storedPart))
                __result = 0;
            else
                __result = storedPart.stackCapacity;

            return false;
        }

        // TODO : if we want an API that prevent parts being stackable, this is where the check should
        // be to avoid the slot UI from showing the stack amount / bar
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ModuleInventoryPart), nameof(ModuleInventoryPart.IsStackable))]
        static bool ModuleInventoryPart_IsStackable_Prefix(ModuleInventoryPart __instance, int slotIndex, ref bool __result)
        {
            if (!__instance.storedParts.TryGetValue(slotIndex, out StoredPart storedPart) || !IsStackable(storedPart))
                __result = false;
            else
                __result = true;

            return false;
        }

        // Note : this isn't used anywhere in the stock code
        // TODO : there is a risk of that getter being inlined... We need additional testing there...
        [HarmonyPrefix]
        [HarmonyPatch(typeof(StoredPart), nameof(StoredPart.CanStack), MethodType.Getter)]
        static bool StoredPart_CanStack_Prefix(StoredPart __instance, ref bool __result)
        {
            __result = IsStackable(__instance);
            return false;
        }

        // Note : this isn't used anywhere in the stock code
        // TODO : there is a risk of that getter being inlined... We need additional testing there...
        [HarmonyPrefix]
        [HarmonyPatch(typeof(StoredPart), nameof(StoredPart.IsFull), MethodType.Getter)]
        static bool StoredPart_IsFull_Prefix(StoredPart __instance, ref bool __result)
        {
            __result = IsStackable(__instance) && __instance.quantity < __instance.stackCapacity;
            return false;
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(ModuleInventoryPart), "PartDroppedOnInventory")]
        static IEnumerable<CodeInstruction> ModuleInventoryPart_PartDroppedOnInventory_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            MethodInfo CanStackInSlotOrig = AccessTools.Method(typeof(ModuleInventoryPart), nameof(ModuleInventoryPart.CanStackInSlot));
            MethodInfo CanStackInSlotModif = AccessTools.Method(typeof(InventoryAPI.Utils), nameof(InventoryAPI.Utils.CanStackInSlot));
            // method : PartDroppedOnInventory(ConstructionEventType evt, Part p)

            // if (CanStackInSlot(p.partInfo, partVariantName, slotIndex))
            //IL_0122: ldarg.0
            //IL_0123: ldarg.2
            //IL_0124: ldfld class AvailablePart Part::partInfo
            //IL_0129: ldloc.3
            //IL_012a: ldloc.s 5
            //IL_012c: call instance bool ModuleInventoryPart::CanStackInSlot(class AvailablePart, string, int32)
            //IL_0131: brtrue.s IL_014f

            // replace with :
            // if (PartEquality.CanStackInSlot(p, this, slotIndex))
            //IL_0123: ldarg.2
            //IL_0124: ldarg.0
            //IL_012a: ldloc.s 5
            //IL_012c: call bool KSPCommunityFixes.PartEquality::CanStackInSlot(Part, ModuleInventoryPart, int)
            //IL_0131: brtrue.s IL_014f

            for (int i = 0; i < code.Count - 5; i++)
            {
                if (code[i].opcode == OpCodes.Ldarg_0 // remove
                    && code[i + 1].opcode == OpCodes.Ldarg_2 // keep
                    && code[i + 2].opcode == OpCodes.Ldfld // change to Ldarg_0 and remove operand
                    && code[i + 3].opcode == OpCodes.Ldloc_3 // remove
                    && code[i + 4].opcode == OpCodes.Ldloc_S // keep
                    && code[i + 5].opcode == OpCodes.Call && code[i + 5].operand == CanStackInSlotOrig) // change operand
                {
                    code[i].opcode = OpCodes.Nop;
                    code[i + 2].opcode = OpCodes.Ldarg_0;
                    code[i + 2].operand = null;
                    code[i + 3].opcode = OpCodes.Nop;
                    code[i + 5].operand = CanStackInSlotModif;
                }
            }

            return code;
        }

        //static IEnumerable<CodeInstruction> ModuleInventoryPart_PreviewLimits_Transpiler(IEnumerable<CodeInstruction> instructions)
        //{
        //    List<CodeInstruction> code = new List<CodeInstruction>(instructions);

        //    MethodInfo CanStackInSlotOrig = AccessTools.Method(typeof(ModuleInventoryPart), nameof(ModuleInventoryPart.CanStackInSlot));
        //    MethodInfo CanStackInSlotModif = AccessTools.Method(typeof(CargoUtils), nameof(CargoUtils.CanStackInSlot));

        //    // method : PreviewLimits(Part newPart, int amountToStore, AvailablePart slotPart, int slotIndex)

        //    // if (!CanStackInSlot(newPart.partInfo, partVariantName, slotIndex))
        //    //IL_0096: ldarg.0
        //    //IL_0097: ldarg.1
        //    //IL_0098: ldfld class AvailablePart Part::partInfo
        //    //IL_009d: ldloc.3
        //    //IL_009e: ldarg.s slotIndex
        //    //IL_00a0: call instance bool ModuleInventoryPart::CanStackInSlot(class AvailablePart, string, int32)
        //    //IL_00a5: brtrue.s IL_0116

        //    // replace with :
        //    // if (PartEquality.CanStackInSlot(newPart, this, slotIndex))
        //    //IL_0123: ldarg.1
        //    //IL_0124: ldarg.0
        //    //IL_012a: ldarg.s slotIndex
        //    //IL_012c: call bool KSPCommunityFixes.PartEquality::CanStackInSlot(Part, ModuleInventoryPart, int)
        //    //IL_0131: brtrue.s IL_0116

        //    for (int i = 0; i < code.Count - 5; i++)
        //    {
        //        if (code[i].opcode == OpCodes.Ldarg_0 // remove
        //            && code[i + 1].opcode == OpCodes.Ldarg_1 // keep
        //            && code[i + 2].opcode == OpCodes.Ldfld // change to Ldarg_0 and remove operand
        //            && code[i + 3].opcode == OpCodes.Ldloc_3 // remove
        //            && code[i + 4].opcode == OpCodes.Ldarg_S // keep
        //            && code[i + 5].opcode == OpCodes.Call && code[i + 5].operand == CanStackInSlotOrig) // change operand
        //        {
        //            code[i].opcode = OpCodes.Nop;
        //            code[i + 2].opcode = OpCodes.Ldarg_0;
        //            code[i + 2].operand = null;
        //            code[i + 3].opcode = OpCodes.Nop;
        //            code[i + 5].operand = CanStackInSlotModif;
        //            code.RemoveAt(i + 3);
        //            code.RemoveAt(i);
        //        }
        //    }

        //    return code;
        //}

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(UIPartActionInventorySlot), "ProcessClickWithHeldPart")]
        static IEnumerable<CodeInstruction> UIPartActionInventorySlot_ProcessClickWithHeldPart_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            MethodInfo CanStackInSlotOrig = AccessTools.Method(typeof(ModuleInventoryPart), nameof(ModuleInventoryPart.CanStackInSlot));
            MethodInfo CanStackInSlotModif = AccessTools.Method(typeof(InventoryAPI.Utils), nameof(InventoryAPI.Utils.CanStackInSlot));
            FieldInfo moduleInventoryPart = AccessTools.Field(typeof(UIPartActionInventorySlot), "moduleInventoryPart");

            // if (moduleInventoryPart.CanStackInSlot(currentCargoPart.partInfo, text, slotIndex))
            // ldarg.0
            // ldfld class ModuleInventoryPart UIPartActionInventorySlot::moduleInventoryPart
            // ldloc.2
            // ldfld class AvailablePart Part::partInfo
            // ldloc.1
            // ldarg.0
            // ldfld int32 UIPartActionInventorySlot::slotIndex
            // callvirt instance bool ModuleInventoryPart::CanStackInSlot(class AvailablePart, string, int32)
            // brfalse.s IL_022b

            // replace with :
            // if (PartEquality.CanStackInSlot(newPart, this, slotIndex))
            // ldloc.2
            // ldarg.0
            // ldfld class ModuleInventoryPart UIPartActionInventorySlot::moduleInventoryPart
            // ldarg.0
            // ldfld int32 UIPartActionInventorySlot::slotIndex
            // call bool KSPCommunityFixes.PartEquality::CanStackInSlot(Part, ModuleInventoryPart, int)
            // brfalse.s IL_022b

            for (int i = 0; i < code.Count - 7; i++)
            {
                if (code[i].opcode == OpCodes.Ldarg_0 // remove
                    && code[i + 1].opcode == OpCodes.Ldfld // remove
                    && code[i + 2].opcode == OpCodes.Ldloc_2 // keep
                    && code[i + 3].opcode == OpCodes.Ldfld // change to Ldarg_0 and remove operand
                    && code[i + 4].opcode == OpCodes.Ldloc_1 // change to ldfld moduleInventoryPart
                    && code[i + 5].opcode == OpCodes.Ldarg_0 // keep
                    && code[i + 6].opcode == OpCodes.Ldfld // keep
                    && code[i + 7].opcode == OpCodes.Callvirt && code[i + 7].operand == CanStackInSlotOrig) // change call & operand
                {
                    code[i].opcode = OpCodes.Nop;
                    code[i + 1].opcode = OpCodes.Nop;
                    code[i + 1].operand = null;
                    code[i + 3].opcode = OpCodes.Ldarg_0;
                    code[i + 3].operand = null;
                    code[i + 4].opcode = OpCodes.Ldfld;
                    code[i + 4].operand = moduleInventoryPart;
                    code[i + 7].opcode = OpCodes.Call;
                    code[i + 7].operand = CanStackInSlotModif;
                }
            }

            return code;
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(EditorPartIcon), "OnEditorPartEvent")]
        static IEnumerable<CodeInstruction> EditorPartIcon_OnEditorPartEvent_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            MethodInfo CanStackInSlotOrig = AccessTools.Method(typeof(UIPartActionInventorySlot), nameof(UIPartActionInventorySlot.CanStackInSlot));
            MethodInfo CanStackInSlotModif = AccessTools.Method(typeof(InventoryAPI.Utils), nameof(InventoryAPI.Utils.CanStackInSlot));
            FieldInfo UPAIS = AccessTools.Field(typeof(EditorPartIcon), "UIPAIS");
            FieldInfo slotIndex = AccessTools.Field(typeof(UIPartActionInventorySlot), nameof(UIPartActionInventorySlot.slotIndex));
            FieldInfo moduleInventoryPart = AccessTools.Field(typeof(UIPartActionInventorySlot), "moduleInventoryPart");

            // method : OnEditorPartEvent(ConstructionEventType evt, Part p)

            // bool active = UIPAIS.CanStackInSlot(p.partInfo, partVariantName);
            // ldarg.0
            // ldfld class UIPartActionInventorySlot KSP.UI.Screens.EditorPartIcon::UIPAIS
            // ldarg.2
            // ldfld class AvailablePart Part::partInfo
            // ldloc.0
            // callvirt instance bool UIPartActionInventorySlot::CanStackInSlot(class AvailablePart, string)
            // stloc.1

            // replace with :
            // bool active = PartEquality.CanStackInSlot(p, UIPAIS, UIPAIS.slotIndex)
            // ldarg.2
            // ldarg.0
            // ldfld class UIPartActionInventorySlot KSP.UI.Screens.EditorPartIcon::UIPAIS
            // ldfld class ModuleInventoryPart UIPartActionInventorySlot::moduleInventoryPart
            // ldarg.0
            // ldfld class UIPartActionInventorySlot KSP.UI.Screens.EditorPartIcon::UIPAIS
            // ldfld int32 UIPartActionInventorySlot::slotIndex
            // call bool KSPCommunityFixes.PartEquality::CanStackInSlot(p, UIPAIS, UIPAIS.slotIndex)
            // brfalse.s IL_022b

            for (int i = 1; i < code.Count - 5; i++)
            {
                if (code[i].opcode == OpCodes.Ldarg_0 // change to ldarg_2 and add Ldarg_0 after (do not insert before because labels)
                    && code[i + 1].opcode == OpCodes.Ldfld // keep
                    && code[i + 2].opcode == OpCodes.Ldarg_2 // change to Ldarg_0, insert ldfld moduleInventoryPart before
                    && code[i + 3].opcode == OpCodes.Ldfld // change operand to UPAIS
                    && code[i + 4].opcode == OpCodes.Ldloc_0 // change to ldfld slotIndex
                    && code[i + 5].opcode == OpCodes.Callvirt && code[i + 5].operand == CanStackInSlotOrig) // change call & operand
                {
                    code[i].opcode = OpCodes.Ldarg_2;
                    code[i + 2].opcode = OpCodes.Ldarg_0;
                    code[i + 3].operand = UPAIS;
                    code[i + 4].opcode = OpCodes.Ldfld;
                    code[i + 4].operand = slotIndex;
                    code[i + 5].opcode = OpCodes.Call;
                    code[i + 5].operand = CanStackInSlotModif;
                    code.Insert(i + 2, new CodeInstruction(OpCodes.Ldfld, moduleInventoryPart));
                    code.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_0));
                }
            }

            return code;
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(EditorPartIcon), "OnInventoryPartOnMouseChanged")]
        static IEnumerable<CodeInstruction> EditorPartIcon_OnInventoryPartOnMouseChanged_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            MethodInfo CanStackInSlotOrig = AccessTools.Method(typeof(UIPartActionInventorySlot), nameof(UIPartActionInventorySlot.CanStackInSlot));
            MethodInfo CanStackInSlotModif = AccessTools.Method(typeof(InventoryAPI.Utils), nameof(InventoryAPI.Utils.CanStackInSlot));
            FieldInfo UPAIS = AccessTools.Field(typeof(EditorPartIcon), "UIPAIS");
            FieldInfo slotIndex = AccessTools.Field(typeof(UIPartActionInventorySlot), nameof(UIPartActionInventorySlot.slotIndex));
            FieldInfo moduleInventoryPart = AccessTools.Field(typeof(UIPartActionInventorySlot), "moduleInventoryPart");

            // method : OnInventoryPartOnMouseChanged(Part p)

            // bool active = UIPAIS.CanStackInSlot(p.partInfo, partVariantName);
            // ldarg.0
            // ldfld class UIPartActionInventorySlot KSP.UI.Screens.EditorPartIcon::UIPAIS
            // ldarg.1
            // ldfld class AvailablePart Part::partInfo
            // ldloc.0
            // callvirt instance bool UIPartActionInventorySlot::CanStackInSlot(class AvailablePart, string)
            // stloc.1

            // replace with :
            // bool active = PartEquality.CanStackInSlot(p, UIPAIS, UIPAIS.slotIndex)
            // ldarg.1
            // ldarg.0
            // ldfld class UIPartActionInventorySlot KSP.UI.Screens.EditorPartIcon::UIPAIS
            // ldfld class ModuleInventoryPart UIPartActionInventorySlot::moduleInventoryPart
            // ldarg.0
            // ldfld class UIPartActionInventorySlot KSP.UI.Screens.EditorPartIcon::UIPAIS
            // ldfld int32 UIPartActionInventorySlot::slotIndex
            // call bool KSPCommunityFixes.PartEquality::CanStackInSlot(p, UIPAIS, UIPAIS.slotIndex)
            // brfalse.s IL_022b

            for (int i = 0; i < code.Count - 5; i++)
            {
                if (code[i].opcode == OpCodes.Ldarg_0 // change to ldarg_1 and add Ldarg_0 after (do not insert before because labels)
                    && code[i + 1].opcode == OpCodes.Ldfld // keep
                    && code[i + 2].opcode == OpCodes.Ldarg_1 // change to Ldarg_0, insert ldfld moduleInventoryPart before
                    && code[i + 3].opcode == OpCodes.Ldfld // change operand to UPAIS
                    && code[i + 4].opcode == OpCodes.Ldloc_0 // change to ldfld slotIndex
                    && code[i + 5].opcode == OpCodes.Callvirt && code[i + 5].operand == CanStackInSlotOrig) // change call & operand
                {
                    code[i].opcode = OpCodes.Ldarg_1;
                    code[i + 2].opcode = OpCodes.Ldarg_0;
                    code[i + 3].operand = UPAIS;
                    code[i + 4].opcode = OpCodes.Ldfld;
                    code[i + 4].operand = slotIndex;
                    code[i + 5].opcode = OpCodes.Call;
                    code[i + 5].operand = CanStackInSlotModif;
                    code.Insert(i + 2, new CodeInstruction(OpCodes.Ldfld, moduleInventoryPart));
                    code.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_0));
                }
            }

            return code;
        }

        #endregion
    }
}
