using HarmonyLib;
using InventoryAPI;
using KSP.Localization;
using KSP.UI.Screens.Editor;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace BetterStockInventory
{
    // TODO : remove module infos altogether and add custom widgets instead :
    // - current volume / mass
    // - module list (just the titles)
    // - science data
    // - aggregated resource amount/capacity
    [HarmonyPatch]
    public class BetterCargoTooltip
    {
        private static string[] scienceDataContainerModules;

        public static void ApplyPostPatchFixes()
        {
            List<string> scienceDataContainerNames = new List<string>();
            foreach (AssemblyLoader.LoadedAssembly loadedAssembly in AssemblyLoader.loadedAssemblies)
            {
                foreach (Type type in loadedAssembly.assembly.GetTypes())
                {
                    if (typeof(IScienceDataContainer).IsAssignableFrom(type))
                        scienceDataContainerNames.Add(type.Name);
                }
            }

            scienceDataContainerModules = scienceDataContainerNames.ToArray();
        }

        //[HarmonyPrefix]
        //[HarmonyPatch(typeof(InventoryPartListTooltipController), nameof(InventoryPartListTooltipController.OnPointerEnter))]
        //static bool InventoryPartListTooltipController_OnPointerEnter_Prefix(InventoryPartListTooltipController __instance)
        //{
        //    return false;
        //}

        //[HarmonyPrefix]
        //[HarmonyPatch(typeof(InventoryPartListTooltipController), nameof(InventoryPartListTooltipController.OnPointerExit))]
        //static bool InventoryPartListTooltipController_OnPointerExit_Prefix(InventoryPartListTooltipController __instance, ref bool ___pinned)
        //{
        //    return true;
        //}

        //[HarmonyPrefix]
        //[HarmonyPatch(typeof(InventoryPartListTooltip), "UnityEngine.EventSystems.IPointerEnterHandler.OnPointerEnter")]
        //static bool InventoryPartListTooltip_OnPointerEnter_Prefix(InventoryPartListTooltip __instance)
        //{
        //    return true;
        //}

        //[HarmonyPrefix]
        //[HarmonyPatch(typeof(InventoryPartListTooltip), "UnityEngine.EventSystems.IPointerExitHandler.OnPointerExit")]
        //static bool InventoryPartListTooltip_OnPointerExit_Prefix(InventoryPartListTooltip __instance)
        //{
        //    __instance.mouseOver = false;
        //    UIMasterController.Instance.UnpinTooltip(__instance.toolTipController);
        //    __instance.toolTipController.Unpin();
        //    __instance.toolTipController.OnPointerExit(null);
        //    return false;
        //}

        [HarmonyPostfix]
        [HarmonyPatch(typeof(InventoryPartListTooltip), "CreateInfoWidgets")]
        static void InventoryPartListTooltip_CreateInfoWidgets_Prefix(InventoryPartListTooltip __instance, ref AvailablePart ___partInfo)
        {
            ProtoPartSnapshot protoPartSnapshot = __instance.inventoryStoredPart?.snapshot;
            if (protoPartSnapshot == null)
                return;

            List<WidgetInfo> widgets = new List<WidgetInfo>();
            List<ScienceData> scienceData = new List<ScienceData>();

            for (int i = 0; i < protoPartSnapshot.modules.Count; i++)
            {
                ProtoPartModuleSnapshot protoModule = protoPartSnapshot.modules[i];
                int moduleIndex = i;

                if (Lib.TryFindModulePrefab(protoPartSnapshot, ref moduleIndex, out PartModule modulePrefab))
                {
                    if (modulePrefab is ICargoModuleCustomInfo customInfoModule)
                    {
                        IEnumerable<WidgetInfo> apiWidgets = null;
                        try
                        {
                            apiWidgets = customInfoModule.GetWidgets(__instance.inventoryStoredPart, protoModule);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"Error getting popup widgets for cargo part {__instance.inventoryStoredPart.partName} in module {protoModule.moduleName}\n{e}");
                            apiWidgets = null;
                        }

                        if (apiWidgets != null)
                            widgets.AddRange(apiWidgets);
                    }
                }

                bool isScienceDataModule = false;
                foreach (string moduleName in scienceDataContainerModules)
                {
                    if (protoModule.moduleName == moduleName)
                    {
                        isScienceDataModule = true;
                        break;
                    }
                }

                if (isScienceDataModule)
                {
                    ConfigNode[] scienceDataNodes = protoModule.moduleValues.GetNodes("ScienceData");
                    if (scienceDataNodes != null && scienceDataNodes.Length != 0)
                    {
                        foreach (ConfigNode scienceDataNode in scienceDataNodes)
                        {
                            scienceData.Add(new ScienceData(scienceDataNode));
                        }
                    }
                }
            }

            foreach (WidgetInfo widgetInfo in widgets)
            {
                if (string.IsNullOrEmpty(widgetInfo.title) || string.IsNullOrEmpty(widgetInfo.content))
                    continue;

                CreateWidget(__instance, widgetInfo.title, widgetInfo.content, widgetInfo.color);
            }

            if (scienceData.Count > 0)
                AddScienceDataWidgets(__instance, scienceData);
        }

        static void AddScienceDataWidgets(InventoryPartListTooltip tooltip, List<ScienceData> scienceData)
        {
            foreach (ScienceData data in scienceData)
            {
                ScienceSubject subject = ResearchAndDevelopment.GetSubjectByID(data.subjectID);
                if (subject == null)
                    continue;

                float careerMultiplier = HighLogic.CurrentGame.Parameters.Career.ScienceGainMultiplier;
                float scienceValue = ResearchAndDevelopment.GetScienceValue(data.dataAmount, data.scienceValueRatio, subject) * careerMultiplier;
                // Note : the real transmit value will be higher, in function of the vessel antenna strength bonus.
                // But we have no way to acquire a reference to the right vessel here, so we can't reliably compute it.
                float baseTransmitValue = ResearchAndDevelopment.GetScienceValue(data.dataAmount, data.scienceValueRatio, subject, data.baseTransmitValue) * careerMultiplier;

                StringBuilder sb = StringBuilderCache.Acquire();
                sb.Append(Localizer.Format("#autoLOC_7003000", scienceValue.ToString("0.0"))); // #autoLOC_7003000 = Recovery: +<<1>> Science
                sb.Append("\n");
                sb.Append(Localizer.Format("#autoLOC_7003001", baseTransmitValue.ToString("0.0"))); // #autoLOC_7003001 = Transmit: +<<1>> Science
                sb.Append("\n");
                sb.Append(ResearchAndDevelopment.GetResults(subject.id));
                if (!string.IsNullOrEmpty(data.extraResultString))
                {
                    sb.Append("\n");
                    sb.Append(Localizer.Format(data.extraResultString));
                }

                CreateWidget(tooltip, "Science data: " + data.title, sb.ToStringAndRelease(), new Color(0.427f, 0.812f, 0.965f));
            }
        }

        static void CreateWidget(InventoryPartListTooltip tooltip, string title, string content, Color color = default)
        {
            PartListTooltipWidget widget = tooltip.GetNewTooltipWidget(tooltip.extInfoModuleWidgetPrefab);
            widget.Setup(title, content);
            widget.transform.SetParent(tooltip.extInfoListContainer.transform, false);
            if (color != default)
            {
                Image img = widget.GetComponent<Image>();
                if (img != null)
                    img.color = color;
            }
        }
    }
}
