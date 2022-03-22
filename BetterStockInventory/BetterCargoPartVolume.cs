using HarmonyLib;
using InventoryAPI;
using KSP.Localization;
using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using static InventoryAPI.Utils;
using Debug = UnityEngine.Debug;

namespace BetterStockInventory
{
    [HarmonyPatch]
    class BetterCargoPartVolume
    {
        private const string CARGO_INFO_NAME = "ModuleCargoPartInfo";
        private const string PLUGINDATA_FOLDER = "PluginData";
        private const string CACHE_FILE_NAME = "CargoVolumeCache.cfg";

        private static HashSet<AvailablePart> dynamicVolumeCargoParts = new HashSet<AvailablePart>();
        private static HashSet<AvailablePart> apiVolumeCargoParts = new HashSet<AvailablePart>();
        private static Dictionary<AvailablePart, float> partVolumes = new Dictionary<AvailablePart, float>();
        private static Dictionary<AvailablePart, Dictionary<string, float>> partVariantsVolumes = new Dictionary<AvailablePart, Dictionary<string, float>>();
        private static ConfigNode volumeCache;
        private static bool volumeCacheIsValid;
        private static Stopwatch loadWatch = new Stopwatch();

        public static void ApplyPostPatchFixes()
        {
            // Make ModuleCargoPart.packedVolume persistent
            Lib.EditPartModuleKSPFieldAttributes(
                typeof(ModuleCargoPart),
                nameof(ModuleCargoPart.packedVolume),
                kspField => kspField.isPersistant = true);
        }

        public static void SetCargoPartsVolume()
        {
            loadWatch.Restart();

            dynamicVolumeCargoParts.Clear();
            partVariantsVolumes.Clear();

            LoadVolumeCache();

            string mmSHAPath = Path.Combine(Path.GetFullPath(KSPUtil.ApplicationRootPath), "GameData", "ModuleManager.ConfigSHA");
            ConfigNode mmSHANode = ConfigNode.Load(mmSHAPath);
            string mmSha = null;
            string cacheSHA = null;
            volumeCacheIsValid = mmSHANode != null && mmSHANode.TryGetValue("SHA", ref mmSha) && volumeCache != null && volumeCache.TryGetValue("mmCacheSHA", ref cacheSHA) && mmSha == cacheSHA;
            if (volumeCacheIsValid)
            {
                ParseVolumeCache();
            }

            foreach (AvailablePart availablePart in PartLoader.Instance.loadedParts)
            {
                ModulePartVariants partVariant = availablePart.partPrefab.variants;
                ModuleCargoPart cargoModule = null;

                foreach (PartModule partPrefabModule in availablePart.partPrefab.Modules)
                {
                    if (partPrefabModule is ModuleCargoPart)
                    {
                        cargoModule = (ModuleCargoPart)partPrefabModule;
                    }
                    else if (partPrefabModule is IVariablePackedVolumeModule variableVolumeModule)
                    {
                        bool useMultipleVolumes;
                        try
                        {
                            useMultipleVolumes = variableVolumeModule.InterfaceIsActive;
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[InventoryAPI] Error calling {partPrefabModule.moduleName}.UseMultipleVolume on part prefab {availablePart.name}\n{e}");
                            useMultipleVolumes = false;
                        }

                        if (useMultipleVolumes)
                        {
                            dynamicVolumeCargoParts.Add(availablePart);
                            apiVolumeCargoParts.Add(availablePart);
                        }
                    }
                }

                if (cargoModule == null)
                    continue;

                if (cargoModule.packedVolume > 0f || cargoModule.packedVolume == -1f)
                {
                    partVolumes[availablePart] = cargoModule.packedVolume;
                    continue;
                }

                float maxVolume = cargoModule.packedVolume < -1f ? Math.Abs(cargoModule.packedVolume) : float.MaxValue;

                if (partVariant != null)
                {
                    Dictionary<string, float> variantVolumes;

                    PartVariant baseVariant = partVariant.part.baseVariant;
                    if (baseVariant == null)
                        baseVariant = partVariant.variantList[0];

                    if (!partVariantsVolumes.TryGetValue(availablePart, out variantVolumes))
                        variantVolumes = new Dictionary<string, float>();

                    List<string> automaticVolumeVariants = new List<string>();
                    bool cacheIsValid = true;
                    foreach (PartVariant variant in partVariant.variantList)
                    {
                        if (variantVolumes.ContainsKey(variant.Name))
                            continue;

                        string volumeInfo = variant.GetExtraInfoValue("packedVolume");
                        if (!string.IsNullOrEmpty(volumeInfo) && float.TryParse(volumeInfo, out float variantVolume))
                        {
                            cacheIsValid = false;
                            variantVolume = variantVolume > maxVolume ? -1f : variantVolume;
                            variantVolumes[variant.Name] = variantVolume;
                        }
                        else if (variant.InfoGameObjects.Count > 0 || variant.Name == baseVariant.Name)
                        {
                            cacheIsValid = false;
                            automaticVolumeVariants.Add(variant.Name);
                        }
                    }

                    if (automaticVolumeVariants.Count > 0)
                    {
                        partVariant.gameObject.SetActive(true);
                        try
                        {
                            foreach (string variant in automaticVolumeVariants)
                            {
                                partVariant.SetVariant(variant);
                                float volume = GetPrefabPackedVolume(partVariant.part);
                                if (volume <= 0f)
                                {
                                    Debug.LogWarning($"[BetterCargoPartVolume] Unable to find volume for variant {variant} in {partVariant.part.name}");
                                }
                                else
                                {
                                    Debug.Log($"[BetterCargoPartVolume] Automatic volume for variant {variant} in {partVariant.part.name} : {volume:0.0}L");
                                }

                                volume = volume > maxVolume ? -1f : volume;
                                variantVolumes[variant] = volume;
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[BetterCargoPartVolume] Unable to find volume for {partVariant.part.name}\n{e}");
                            cargoModule.packedVolume = -1f;
                        }
                        finally
                        {
                            partVariant.gameObject.SetActive(false);
                            partVariant.SetVariant(baseVariant.Name);
                        }
                    }

                    if (variantVolumes.Count == 0 || !variantVolumes.ContainsKey(baseVariant.Name))
                    {
                        cargoModule.packedVolume = -1f;
                    }
                    else if (variantVolumes.Count == 1)
                    {
                        cargoModule.packedVolume = variantVolumes[baseVariant.Name];
                        if (!cacheIsValid)
                        {
                            partVariantsVolumes[availablePart] = variantVolumes;
                            volumeCacheIsValid = false;
                        }
                    }
                    else
                    {
                        cargoModule.packedVolume = variantVolumes[baseVariant.Name];

                        foreach (KeyValuePair<string, float> variantVolume in variantVolumes)
                        {
                            if (variantVolume.Value != cargoModule.packedVolume)
                            {
                                dynamicVolumeCargoParts.Add(availablePart);
                                break;
                            }
                        }

                        if (!cacheIsValid)
                        {
                            partVariantsVolumes[availablePart] = variantVolumes;
                            volumeCacheIsValid = false;
                        }
                    }
                }
                else
                {
                    if (partVolumes.TryGetValue(availablePart, out float volume))
                    {
                        volume = volume > maxVolume ? -1f : volume;
                        cargoModule.packedVolume = volume;
                    }
                    else
                    {
                        cargoModule.gameObject.SetActive(true);
                        try
                        {
                            volume = GetPrefabPackedVolume(cargoModule.part);
                            if (volume <= 0f)
                            {
                                Debug.LogWarning($"[BetterCargoPartVolume] Unable to find volume for {cargoModule.part.name}");
                            }
                            else
                            {
                                Debug.Log($"[BetterCargoPartVolume] Automatic volume for {cargoModule.part.name} : {volume:0.0}L");
                            }

                            volume = volume > maxVolume ? -1f : volume;
                            cargoModule.packedVolume = volume;
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[BetterCargoPartVolume] Unable to find volume for {cargoModule.part.name}\n{e}");
                            cargoModule.packedVolume = -1f;
                        }
                        finally
                        {
                            cargoModule.gameObject.SetActive(false);
                        }

                        partVolumes[availablePart] = volume;
                        volumeCacheIsValid = false;
                    }
                }

                // don't update info for ModuleCargoPart derivatives (ModuleGroundPart...)
                if (!cargoModule.GetType().IsSubclassOf(typeof(ModuleCargoPart)))
                {
                    try
                    {
                        UpdateVolumeInfo(availablePart, cargoModule);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[BetterCargoPartVolume] Couldn't update module info for {cargoModule.part.name}\n{e}");
                    }
                }
            }

            int volumeCount = partVolumes.Count + partVariantsVolumes.Count;
            if (volumeCacheIsValid)
            {
                Debug.Log($"[BetterCargoPartVolume] Applied cached cargo volume for {volumeCount} parts");
            }
            else
            {
                Debug.Log($"[BetterCargoPartVolume] Generated cargo volume for {volumeCount} parts");

                if (!string.IsNullOrEmpty(mmSha))
                {
                    volumeCache = new ConfigNode();
                    volumeCache.AddValue("mmCacheSHA", mmSha);

                    foreach (KeyValuePair<AvailablePart, float> cargoPartVolume in partVolumes)
                    {
                        volumeCache.AddValue(cargoPartVolume.Key.name.Replace('.', '_'), cargoPartVolume.Value);
                    }

                    foreach (KeyValuePair<AvailablePart, Dictionary<string, float>> cargoVariantPartVolume in partVariantsVolumes)
                    {
                        ConfigNode apNode = volumeCache.AddNode(cargoVariantPartVolume.Key.name.Replace('.', '_'));
                        foreach (KeyValuePair<string, float> variant in cargoVariantPartVolume.Value)
                        {
                            apNode.AddValue(variant.Key, variant.Value);
                        }
                    }

                    SaveVolumeCache();
                    Debug.Log($"[BetterCargoPartVolume] Cargo volume cache has been created");
                }
                else
                {
                    Debug.LogWarning($"[BetterCargoPartVolume] Cargo volume cache couldn't be created as ModuleManager couldn't create the config cache");
                }
            }

            loadWatch.Stop();
            Debug.Log($"[BetterCargoPartVolume] Loading operations took {loadWatch.ElapsedMilliseconds * 0.001:0.000}s");
        }

        private static void LoadVolumeCache()
        {
            string path = Path.Combine(BetterStockInventoryLoader.ModPath, PLUGINDATA_FOLDER, CACHE_FILE_NAME);

            if (File.Exists(path))
            {
                ConfigNode node = ConfigNode.Load(path);
                if (node?.nodes[0] != null)
                    volumeCache = node.nodes[0];
            }
        }

        private static void SaveVolumeCache()
        {
            string pluginDataPath = Path.Combine(BetterStockInventoryLoader.ModPath, PLUGINDATA_FOLDER);

            if (!Directory.Exists(pluginDataPath))
            {
                Directory.CreateDirectory(pluginDataPath);
            }

            ConfigNode topNode = new ConfigNode();
            topNode.AddNode("CargoVolumeCache", volumeCache);
            topNode.Save(Path.Combine(pluginDataPath, CACHE_FILE_NAME));
        }

        private static void ParseVolumeCache()
        {
            // first value is the mm SHA, skip it
            for (int i = 1; i < volumeCache.values.Count; i++)
            {
                ConfigNode.Value value = volumeCache.values[i];
                AvailablePart ap = PartLoader.getPartInfoByName(value.name.Replace('_', '.'));
                if (ap == null || !float.TryParse(value.value, out float volume))
                {
                    volumeCacheIsValid = false;
                }
                else
                {
                    partVolumes[ap] = volume;
                }
            }

            foreach (ConfigNode node in volumeCache.nodes)
            {
                AvailablePart ap = PartLoader.getPartInfoByName(node.name.Replace('_', '.'));
                if (ap == null)
                {
                    volumeCacheIsValid = false;
                }
                else
                {
                    Dictionary<string, float> variantsVolume = new Dictionary<string, float>();
                    foreach (ConfigNode.Value variantValue in node.values)
                    {
                        if (float.TryParse(variantValue.value, out float volume))
                        {
                            variantsVolume[variantValue.name] = volume;
                        }
                    }

                    partVariantsVolumes[ap] = variantsVolume;
                }
            }
        }

        // update ModuleCargoPart module info in the prefab
        private static void UpdateVolumeInfo(AvailablePart availablePart, ModuleCargoPart cargoModule)
        {
            foreach (AvailablePart.ModuleInfo moduleInfo in availablePart.moduleInfos)
            {
                if (moduleInfo.info == CARGO_INFO_NAME)
                {
                    moduleInfo.info = GetCargoModuleInfo(cargoModule);
                }
            }
        }

        // Since module infos are compiled during prefab compilation, and we need to compute volumes after, we set a placeholder
        // module info that we overwrite after volumes have been loaded/computed. See UpdateVolumeInfo().
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ModuleCargoPart), nameof(ModuleCargoPart.GetInfo))]
        private static bool ModuleCargoPart_GetInfo_Prefix(ModuleCargoPart __instance, ref string __result)
        {
            if ((__instance.packedVolume == 0f || __instance.packedVolume < -1f) && (HighLogic.LoadedScene == GameScenes.LOADING || PartLoader.Instance.Recompile))
            {
                __result = CARGO_INFO_NAME;
            }
            else
            {
                __result = GetCargoModuleInfo(__instance);
            }

            return false;
        }

        // Implementation of the ModuleCargoPart.GetInfo() override :
        // - More compact than the stock widget
        // - List each volume for every variant
        // - Allow IVariablePackedVolumeModule consumers to override the contents
        private static string GetCargoModuleInfo(ModuleCargoPart cargoModule)
        {
            bool isDynamicVolume = false;
            AvailablePart ap = cargoModule.part?.partInfo;
            if (ap != null)
            {
                if (dynamicVolumeCargoParts.Contains(ap))
                {
                    if (partVariantsVolumes.TryGetValue(ap, out Dictionary<string, float> variants))
                    {
                        if (variants.Count > 1)
                        {
                            List<KeyValuePair<string, float>> variantList = new List<KeyValuePair<string, float>>(variants.Count);
                            foreach (PartVariant partVariant in cargoModule.part.variants.variantList)
                            {
                                if (variants.TryGetValue(partVariant.Name, out float volume))
                                {
                                    variantList.Add(new KeyValuePair<string, float>(partVariant.DisplayName, volume));
                                }
                            }

                            variantList.Sort((x, y) => x.Value.CompareTo(y.Value));
                            StringBuilder variantSb = StringBuilderCache.Acquire();
                            int count = variantList.Count;
                            int lastVolume = 0;
                            for (int i = 0; i < count; i++)
                            {
                                KeyValuePair<string, float> variant = variantList[i];

                                if (lastVolume == 0 && variant.Value == -1f)
                                {
                                    variantSb.Append("<b><color=#99ff00ff>");
                                    variantSb.Append(Localizer.Format("#autoLOC_6002642")); // #autoLOC_6002642 = Construction Only Part
                                    variantSb.Append(":</b></color>\n");
                                    lastVolume = 1;
                                }

                                if ((lastVolume == 0 || lastVolume == 1) && variant.Value > 0f)
                                {
                                    variantSb.Append("<b><color=#99ff00ff>");
                                    variantSb.Append(Localizer.Format("#autoLOC_8003414")); // #autoLOC_8003414 = Packed Volume
                                    variantSb.Append(":</b></color>\n");
                                    lastVolume = 2;
                                }

                                variantSb.Append(variant.Key);
                                if (variant.Value > 0f)
                                {
                                    variantSb.Append(": ");
                                    variantSb.Append(variant.Value.ToString("0.0 L"));
                                }

                                if (i < count)
                                    variantSb.Append("\n");
                            }

                            return variantSb.ToStringAndRelease();
                        }
                    }
                    else
                    {
                        isDynamicVolume = true;
                    }
                }
            }

            if (!isDynamicVolume && cargoModule.packedVolume <= 0f)
                return Localizer.Format("#autoLOC_6002642"); // #autoLOC_6002642 = Construction Only Part

            if (isDynamicVolume)
            {
                foreach (PartModule partModule in cargoModule.part.Modules)
                {
                    if (partModule is IVariablePackedVolumeModule variableVolumeModule)
                    {
                        string info = null;
                        try
                        {
                            info = variableVolumeModule.CargoModuleInfo();
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"Unable to get cargo module info for {cargoModule.part.name} by calling {partModule.moduleName}.CargoModuleInfo() :\n{e}");
                        }

                        if (!string.IsNullOrEmpty(info))
                        {
                            return info;
                        }
                    }
                }
            }

            StringBuilder sb = StringBuilderCache.Acquire();

            sb.Append(Localizer.Format("#autoLOC_8003414")); // #autoLOC_8003414 = Packed Volume
            sb.Append(": ");

            if (isDynamicVolume)
                sb.Append(Localizer.Format("#autoLOC_7000057")); // #autoLOC_7000057 = Dynamic
            else
                sb.Append(cargoModule.packedVolume.ToString("0.0 L"));

            return sb.ToStringAndRelease();
        }

        /// <summary>
        /// Update the ModuleCargoPart.packedVolume field, accounting for dynamic volume from either the stock ModulePartVariant module
        /// or modules implementing the IVariablePackedVolumeModule API.
        /// </summary>
        private static void UpdateDynamicPackedVolume(ModuleCargoPart cargoModule)
        {
            float defaultVolume = float.NaN;
            if (cargoModule.part.variants != null && partVariantsVolumes.TryGetValue(cargoModule.part.partInfo, out Dictionary<string, float> variantVolumes))
            {
                if (cargoModule.part.variants.SelectedVariant != null && variantVolumes.TryGetValue(cargoModule.part.variants.SelectedVariant.Name, out defaultVolume))
                {
                    cargoModule.packedVolume = defaultVolume;
                }
                else if (cargoModule.part.baseVariant != null && variantVolumes.TryGetValue(cargoModule.part.baseVariant.Name, out defaultVolume))
                {
                    cargoModule.packedVolume = defaultVolume;
                }
            }

            if (apiVolumeCargoParts.Contains(cargoModule.part.partInfo))
            {
                bool isVariant = true;
                if (defaultVolume == float.NaN)
                {
                    isVariant = false;

                    if (!partVolumes.TryGetValue(cargoModule.part.partInfo, out defaultVolume))
                    {
                        cargoModule.packedVolume = -1f;
                        return;
                    }
                }

                foreach (PartModule partModule in cargoModule.part.Modules)
                {
                    if (partModule is IVariablePackedVolumeModule variableVolumeModule)
                    {
                        float apiVolume;
                        try
                        {
                            apiVolume = variableVolumeModule.CurrentPackedVolume(defaultVolume, isVariant);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"Unable to get packed volume for {cargoModule.part.name} by calling {partModule.moduleName}.GetCargoVolume() :\n{e}");
                            apiVolume = -1f;
                        }

                        if (apiVolume <= 0f || float.IsInfinity(apiVolume) || float.IsNaN(apiVolume))
                            apiVolume = -1f;

                        cargoModule.packedVolume = apiVolume;
                    }
                }
            }
        }


        #region dynamic packedVolume handling
        // region summary : 
        // Patch everything that read from ModuleCargoPart.packedVolume
        // In case the cargo part uses a dynamic volume, force a volume update before processing

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ModuleInventoryPart), "PartDroppedOnInventory")]
        private static void ModuleInventoryPart_PartDroppedOnInventory_Prefix(Part p)
        {
            ModuleCargoPart moduleCargoPart = p.FindModuleImplementing<ModuleCargoPart>();

            if (moduleCargoPart != null)
                UpdateDynamicPackedVolume(moduleCargoPart);
        }

        // We completely override the method to : 
        // - avoid relying on the stored part prefab for mass/volume (use the actual protopart values instead)
        // - update dynamic volume modules
        // - call our custom stacking logic
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ModuleInventoryPart), "PreviewLimits")]
        private static bool ModuleInventoryPart_PreviewLimits_Prefix(ModuleInventoryPart __instance, Part newPart, int amountToStore, AvailablePart slotPart, int slotIndex)
        {
            ModuleCargoPart moduleCargoPart = newPart.FindModuleImplementing<ModuleCargoPart>();

            // update the packed volume of the current "held" part
            if (moduleCargoPart != null)
                UpdateDynamicPackedVolume(moduleCargoPart);

            // if the part is in cargo mode, its mass was never updated to account for IPartMassModifier modules.
            // TODO : a better fix for that would be to update mass when cargo state is set
            if (newPart.State == PartStates.CARGO)
                newPart.UpdateMass();

            // compute available volume/capacity, accounting for the current "held" part
            if (slotPart == null)
            {
                if (moduleCargoPart.packedVolume < 0f)
                {
                    __instance.volumeCapacity = __instance.packedVolumeLimit * 2f;
                }
                else
                {
                    __instance.volumeCapacity += moduleCargoPart.packedVolume * amountToStore;
                }
                __instance.massCapacity += __instance.GetPartMass(newPart) * amountToStore;
            }
            else
            {
                StoredPart storedPart = __instance.storedParts[slotIndex];
                int stackAmountAtSlot = storedPart.quantity;
                if (!CanStackStoredPart(newPart, storedPart))
                {
                    if (moduleCargoPart.packedVolume < 0f)
                    {
                        __instance.volumeCapacity = __instance.packedVolumeLimit * 2f;
                    }
                    else
                    {
                        float cargoVolume = GetPartCargoVolume(storedPart.snapshot);
                        __instance.volumeCapacity = __instance.volumeCapacity + moduleCargoPart.packedVolume * amountToStore - cargoVolume * stackAmountAtSlot;
                    }
                    float cargoMass = GetPartMass(storedPart.snapshot);
                    __instance.massCapacity = __instance.massCapacity + __instance.GetPartMass(newPart) * amountToStore - cargoMass * stackAmountAtSlot;
                }
                else
                {
                    float num = Math.Min(amountToStore + stackAmountAtSlot, moduleCargoPart.stackableQuantity);
                    float num2;
                    float num3;
                    if (moduleCargoPart.packedVolume < 0f)
                    {
                        num2 = __instance.packedVolumeLimit * 2f;
                        num3 = num2;
                    }
                    else
                    {
                        num2 = moduleCargoPart.packedVolume * stackAmountAtSlot;
                        num3 = moduleCargoPart.packedVolume * num;
                    }
                    __instance.volumeCapacity = __instance.volumeCapacity + num3 - num2;
                    float newPartMass = __instance.GetPartMass(newPart);
                    float num4 = newPartMass * stackAmountAtSlot;
                    float num5 = newPartMass * num;
                    __instance.massCapacity = __instance.massCapacity + num5 - num4;
                }
            }
            __instance.showPreview = false;

            __instance.UpdateMassVolumeDisplay(false, moduleCargoPart.packedVolume < 0f);

            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ModuleInventoryPart), "HasCapacity")]
        private static void ModuleInventoryPart_HasCapacity_Prefix(Part newPart)
        {
            ModuleCargoPart moduleCargoPart = newPart.FindModuleImplementing<ModuleCargoPart>();

            if (moduleCargoPart != null)
                UpdateDynamicPackedVolume(moduleCargoPart);
        }

        // special case : the stock method use the prefab values for mass/volume, which will fail to be accurate in so many ways that I can't count them.
        // We completely override that method, using the actual stored mass/volume from the protomodule.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ModuleInventoryPart), "UpdateCapacityValues")]
        private static bool ModuleInventoryPart_UpdateCapacityValues_Prefix(ModuleInventoryPart __instance)
        {
            __instance.volumeOccupied = 0f;
            __instance.massOccupied = 0f;
            for (int i = 0; i < __instance.storedParts.Count; i++)
            {
                StoredPart storedPart = __instance.storedParts.At(i);
                if (storedPart.snapshot != null)
                {
                    __instance.massOccupied += GetPartMass(storedPart.snapshot) * storedPart.quantity;
                    __instance.volumeOccupied += GetPartCargoVolume(storedPart.snapshot) * storedPart.quantity;
                }
            }

            __instance.UpdateMassVolumeDisplay(true, false);

            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIPartActionInventorySlot), "ProcessClickWithHeldPart")]
        private static bool UIPartActionInventorySlot_ProcessClickWithHeldPart_Prefix(UIPartActionInventorySlot __instance)
        {
            Part newPart = UIPartActionControllerInventory.Instance.CurrentCargoPart;
            if (newPart == null)
                return true;

            foreach (Part part in newPart.GetComponentsInChildren<Part>())
            {
                ModuleCargoPart moduleCargoPart = part.FindModuleImplementing<ModuleCargoPart>();

                if (moduleCargoPart != null)
                    UpdateDynamicPackedVolume(moduleCargoPart);
            }

            // bugfix : in the editor, attempting to store the (only) root part in a detached inventory part causes
            // the whole editor to freak out, likely because the "no ship exists" event isn't triggered
            if (HighLogic.LoadedSceneIsEditor)
            {
                if (EditorLogic.RootPart == newPart)
                {
                    ScreenMessages.PostScreenMessage("Cannot store the root part", 5f, ScreenMessageStyle.UPPER_CENTER);
                    return false;
                }
            }

            // bugfix : https://bugs.kerbalspaceprogram.com/issues/28570
            // Steps to reproduce:
            // - Have a single stackable part in hand
            // - store it
            // - hold alt, click to unstore it
            // - while still holding alt, click again to store it
            // - a full stack is stored regardless of volume/ mass limits
            // This happen because ModuleInventoryPart.Update() only trigger a call to ModuleInventoryPart.PreviewLimits()
            // on ALT key up and down events, and when entering / exiting the slot.
            // Since neither of those happen in the above case, the limits aren't applied,
            // and UIPartActionInventorySlot.ProcessClickWithHeldPart() won't take them into account.
            ModuleInventoryPart moduleInventoryPart = __instance.moduleInventoryPart;
            moduleInventoryPart.volumeCapacity = moduleInventoryPart.volumeOccupied;
            moduleInventoryPart.massCapacity = moduleInventoryPart.massOccupied;
            if (GameSettings.MODIFIER_KEY.GetKey())
                UIPartActionControllerInventory.amountToStore = UIPartActionControllerInventory.Instance.CurrentModuleCargoPart.stackableQuantity;
            else
                UIPartActionControllerInventory.amountToStore = UIPartActionControllerInventory.stackSize == 0 ? 1 : UIPartActionControllerInventory.stackSize;

            int slotIndex = __instance.slotIndex;
            moduleInventoryPart.storedParts.TryGetValue(slotIndex, out StoredPart storedPart);
            AvailablePart ap = storedPart?.snapshot?.partInfo;
            ModuleInventoryPart_PreviewLimits_Prefix(moduleInventoryPart, newPart, UIPartActionControllerInventory.amountToStore, ap, slotIndex);
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(EditorPartIcon), nameof(EditorPartIcon.Create))]
        [HarmonyPatch(new Type[] {
            typeof(EditorPartList), typeof(AvailablePart), typeof(StoredPart), typeof(float), typeof(float), typeof(float), typeof(Callback<EditorPartIcon>),
            typeof(bool), typeof(bool), typeof(PartVariant), typeof(bool), typeof(bool)})]
        private static void EditorPartIcon_Create_Postfix(EditorPartIcon __instance)
        {
            if (__instance.btnSwapTexture != null && __instance.inInventory && __instance.AvailPart != null && dynamicVolumeCargoParts.Contains(__instance.AvailPart))
            {
                __instance.btnSwapTexture.gameObject.SetActive(false);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ProtoPartSnapshot), MethodType.Constructor, new Type[] { typeof(Part), typeof(ProtoVessel), typeof(bool) })]
        private static void ProtoPartSnapshot_PartCtor_Postfix(ProtoPartSnapshot __instance, Part PartRef)
        {
            ModuleCargoPart cargoModule = PartRef.FindModuleImplementing<ModuleCargoPart>();
            if (cargoModule != null)
            {
                UpdateDynamicPackedVolume(cargoModule);
                __instance.partStateValues.Add("cargoVolume", new KSPParseable(cargoModule.packedVolume, KSPParseable.Type.FLOAT));
            }
        }

        #endregion
    }
}