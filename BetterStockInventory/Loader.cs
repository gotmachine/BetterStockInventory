using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

namespace BetterStockInventory
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class BetterStockInventoryLoader : MonoBehaviour
    {
        public static Version KspVersion { get; private set; }
        public static Harmony Harmony { get; private set; }
        public static string ModPath { get; private set; }

        void Start()
        {
            KspVersion = new Version(Versioning.version_major, Versioning.version_minor, Versioning.Revision);
            Harmony = new Harmony("KSPCommunityFixes");
            ModPath = Path.GetDirectoryName(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
#if DEBUG
            Harmony.DEBUG = true;
#endif

            GameEvents.OnPartLoaderLoaded.Add(OnPartLoaderLoaded);
            GameEvents.onGameSceneSwitchRequested.Add(OnSceneSwitch);
            GameEvents.onEditorPartEvent.Add(OnEditorPartEvent);
            GameEvents.onPartResourceListChange.Add(OnPartResourceListChange);

            DontDestroyOnLoad(this);
        }

        public void ModuleManagerPostLoad()
        {
            Harmony.PatchAll();
            BetterCargoPartVolume.ApplyPostPatchFixes();
            BetterCargoTooltip.ApplyPostPatchFixes();
        }

        private void OnPartLoaderLoaded()
        {
            BetterCargoPartVolume.SetCargoPartsVolume();
            UnlimitedInventorySlots.OnPartLoaderLoaded();
        }

        private void OnSceneSwitch(GameEvents.FromToAction<GameScenes, GameScenes> data)
        {
            UnlimitedInventorySlotsUI.ClearSettings();
            InventoryAPI.Utils.ClearLastEqualityCheck();
        }

        private void OnEditorPartEvent(ConstructionEventType eventType, Part part)
        {
            switch (eventType)
            {
                case ConstructionEventType.PartAttached:
                case ConstructionEventType.PartDetached:
                case ConstructionEventType.PartDeleted:
                case ConstructionEventType.PartCopied:
                case ConstructionEventType.PartTweaked:
                    InventoryAPI.Utils.ResetLastEqualityCheck();
                    break;
            }
        }

        private void OnPartResourceListChange(Part part)
        {
            ModuleCargoPart instance = part.FindModuleImplementing<ModuleCargoPart>();
            if (instance == null)
                return;

            UnlimitedInventorySlots.UpdateStackableQuantity(instance);
        }
    }
}
