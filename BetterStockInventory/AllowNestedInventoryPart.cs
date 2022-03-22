using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterStockInventory
{
    [HarmonyPatch]
    class AllowNestedInventoryPart
    {
        private static Type moduleCargoPartType = typeof(ModuleCargoPart);

        // Ensure only one cargo module exists on the same part. In case multiple modules are detected,
        // keep the most derived module or if both are the same type, keep the last one by index.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(PartLoader), nameof(PartLoader.ParsePart))]
        static void PartLoader_ParsePart_Prefix(ConfigNode node)
        {
            int nodeCount = node.CountNodes;
            Type lastCargoModuleType = null;
            int lastCargoModuleNodeIndex = 0;

            for (int i = nodeCount - 1; i >= 0; i--)
            {
                if (node.nodes[i].name == "MODULE")
                {
                    string moduleName = node.nodes[i].GetValue("name");
                    if (string.IsNullOrEmpty(moduleName))
                        continue;

                    Type classByName = AssemblyLoader.GetClassByName(typeof(PartModule), moduleName);
                    if (classByName == null)
                        continue;

                    if (classByName == moduleCargoPartType || classByName.IsSubclassOf(moduleCargoPartType))
                    {
                        if (lastCargoModuleType == null)
                        {
                            lastCargoModuleType = classByName;
                            lastCargoModuleNodeIndex = i;
                            continue;
                        }

                        string partName = node.GetValue("name");
                        if (string.IsNullOrEmpty(partName))
                            partName = "unknown part";

                        // remove the least derived module
                        if (classByName.IsSubclassOf(lastCargoModuleType))
                        {
                            Debug.LogWarning($"[BetterStockInventory] Can't have more than one cargo module per part. On {partName}, removing {lastCargoModuleType} at index {lastCargoModuleNodeIndex}, keeping {classByName} at index {i}");
                            
                            node.nodes.nodes.RemoveAt(lastCargoModuleNodeIndex);
                            lastCargoModuleType = classByName;
                            lastCargoModuleNodeIndex = i;
                        }
                        else
                        {
                            Debug.LogWarning($"[BetterStockInventory] Can't have more than one cargo module per part. On {partName}, removing {classByName} at index {i}, keeping {lastCargoModuleType} at index {lastCargoModuleNodeIndex}");

                            node.nodes.nodes.RemoveAt(i);
                            lastCargoModuleNodeIndex--;
                        }
                    }
                }
            }
        }


        // remove the hardcoded checks during part compilation that prevent having a ModuleCargoPart and a ModuleInventoryPart on the same part
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Part), nameof(Part.AddModule), new[] { typeof(string), typeof(bool) })]
        static bool Part_AddModule_Prefix(Part __instance, string moduleName, bool forceAwake, ref PartModule __result)
        {
            Type classByName = AssemblyLoader.GetClassByName(typeof(PartModule), moduleName);
            if (classByName == null)
            {
                Debug.LogError("Cannot find a PartModule of typename '" + moduleName + "'");
                __result = null;
                return false;
            }

            PartModule partModule = (PartModule)__instance.gameObject.AddComponent(classByName);
            if (partModule == null)
            {
                Debug.LogError("Cannot create a PartModule of typename '" + moduleName + "'");
                __result = null;
                return false;
            }
            if (forceAwake)
            {
                partModule.Awake();
            }
            __instance.Modules.Add(partModule);
            __result = partModule;
            return false;
        }

        // Make sure inventory parts can't be stacked
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ModuleInventoryPart), nameof(ModuleInventoryPart.CanStackInSlot))]
        static void ModuleInventoryPart_CanStackInSlot_Postfix(AvailablePart part, ref bool __result)
        {
            if (!__result)
                return;

            if (part.partPrefab.HasModuleImplementing<ModuleInventoryPart>())
            {
                __result = false;
            }
        }

        // Check if inventory part can be stored
        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIPartActionInventorySlot), "ProcessClickWithHeldPart")]
        static bool UIPartActionInventorySlot_ProcessClickWithHeldPart_Prefix(ModuleInventoryPart ___moduleInventoryPart)
        {
            if (___moduleInventoryPart != null)
                return CanBeStored(UIPartActionControllerInventory.Instance.CurrentCargoPart, ___moduleInventoryPart);

            return true;
        }

        // Check if inventory part can be stored
        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIPartActionInventorySlot), "StorePartInEmptySlot")]
        static bool UIPartActionInventorySlot_StorePartInEmptySlot_Prefix(ModuleInventoryPart ___moduleInventoryPart, Part partToStore)
        {
            if (___moduleInventoryPart != null)
                return CanBeStored(partToStore, ___moduleInventoryPart);

            return true;
        }

        private static bool CanBeStored(Part cargoPart, ModuleInventoryPart inventoryModule)
        {
            // Prevent inventory parts to be stored in themselves
            if (inventoryModule.part != null && cargoPart == inventoryModule.part)
            {
                ScreenMessages.PostScreenMessage("Cannot store itself !", 5f, ScreenMessageStyle.UPPER_CENTER);
                return false;
            }

            // Storing a non-empty inventory part isn't allowed
            ModuleInventoryPart cargoPartInventory = cargoPart.FindModuleImplementing<ModuleInventoryPart>();
            if (cargoPartInventory != null && cargoPartInventory.storedParts.Count > 0)
            {
                ScreenMessages.PostScreenMessage($"Cannot store {cargoPart.partInfo.title}\nIts inventory must be empty", 5f, ScreenMessageStyle.UPPER_CENTER);
                return false;
            }

            return true;
        }
    }
}
