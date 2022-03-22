using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace InventoryAPI
{
    public static class Utils
    {
        private static List<string> cargoModulesNames = new List<string>();

        public static void ModuleManagerPostLoad()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (Type type in assembly.GetTypes())
                {
                    if (typeof(ModuleCargoPart).IsAssignableFrom(type))
                    {
                        cargoModulesNames.Add(type.Name);
                    }
                }
            }
        }

        /// <summary>
        /// Get a ProtoPartSnapshot mass
        /// </summary>
        public static float GetPartMass(ProtoPartSnapshot protoPart)
        {
            double mass = protoPart.mass; // ProtoPartSnapshot.mass account for structural mass + modules mass
            foreach (ProtoPartResourceSnapshot protoResource in protoPart.resources)
            {
                if (protoResource?.definition != null)
                    mass += protoResource.amount * protoResource.definition.density;
            }

            return (float)mass;
        }

        /// <summary>
        /// Get a ProtoPartSnapshot cargo volume (accounts for dynamic volume parts)
        /// </summary>
        public static float GetPartCargoVolume(ProtoPartSnapshot protoPart)
        {
            if (protoPart.partStateValues.TryGetValue("cargoVolume", out KSPParseable parseable))
            {
                return parseable.value_float;
            }
            else
            {
                float cargoVolume = 0f;
                foreach (ProtoPartModuleSnapshot protoModule in protoPart.modules)
                {
                    if (cargoModulesNames.Contains(protoModule.moduleName) && protoModule.moduleValues.TryGetValue(nameof(ModuleCargoPart.packedVolume), ref cargoVolume))
                    {
                        return cargoVolume;
                    }
                }
            }

            return 0f;
        }

        /// <summary>
        /// Can a part be stacked in the provided non-empty inventory slot. Returns false if the slot is empty.
        /// This only check if the part is stackable and allowed to be stacked in its current state.
        /// This doesn't check inventory mass/volume limits.
        /// </summary>
        public static bool CanStackInSlot(Part part, ModuleInventoryPart inventoryModule, int slotIndex)
        {
            if (!inventoryModule.storedParts.TryGetValue(slotIndex, out StoredPart storedPart))
                return false;

            if (!IsStackable(storedPart))
                return false;

            return PartEqualsProtopart(part, storedPart.snapshot, true);
        }

        /// <summary>
        /// Can a StoredPart and a Part be stacked together according to the BetterInventory rules
        /// This only check if the parts are in the same configuration, and doesn't check stack size limits,
        /// nor if the part is configured as non-stackable and neither volume / mass constraints.
        /// </summary>
        public static bool CanStackStoredPart(Part part, StoredPart storedPart)
        {
            return PartEqualsProtopart(part, storedPart.snapshot, true);
        }

        /// <summary>
        /// Is the StoredPart allowed to be stacked ?
        /// </summary>
        public static bool IsStackable(StoredPart part)
        {
            if (part.stackCapacity == 0 || part.snapshot == null)
                return false;

            return part.snapshot.resources.Count == 0;
        }

        /// <summary>
        /// Is the part allowed to be stacked ?
        /// </summary>
        public static bool PartIsStackable(Part part)
        {
            if (part.Resources.Count > 0)
                return false;

            ModuleCargoPart cargoModule = part.FindModuleImplementing<ModuleCargoPart>();
            if (cargoModule == null)
                return false;

            return cargoModule.stackableQuantity != 0;
        }

        public static void ResetLastEqualityCheck()
        {
            lastEqualityCheckPartId = 0;
        }

        public static void ClearLastEqualityCheck()
        {
            lastEqualityCheckPartId = 0;
            lastCheckedPartModuleNodes.Clear();
        }

        // Do some caching of the results. Stock check part equality a lot due to the editor events mess,
        // often multiple times for same part.
        private static float lastEqualityCheckTime;
        private static int lastEqualityCheckPartId;
        private static List<ConfigNode> lastCheckedPartModuleNodes = new List<ConfigNode>();

        /// <summary>
        /// Check if a part and a protopart have an identical persisted state
        /// </summary>
        /// <param name="notEqualIfHasResources">if true, parts won't be considered equal if it contains resources</param>
        /// <returns>true if the part and protopart persisted state are identical, false otherwise</returns>
        public static bool PartEqualsProtopart(Part part, ProtoPartSnapshot protoPart, bool notEqualIfHasResources)
        {
            if (part.partInfo.name != protoPart.partName)
                return false;

            if (part.Modules.Count != protoPart.modules.Count)
                return false;

            if (part.variants?.SelectedVariant != null && part.variants.SelectedVariant.Name != protoPart.moduleVariantName)
                return false;

            if (notEqualIfHasResources)
            {
                if (part.Resources.Count > 0 || protoPart.resources.Count > 0)
                    return false;
            }
            else
            {
                if (part.Resources.Count != protoPart.resources.Count)
                    return false;

                for (int i = 0; i < part.Resources.Count; i++)
                {
                    PartResource partRes = part.Resources[i];
                    ProtoPartResourceSnapshot protoRes = protoPart.resources[i];

                    if (partRes.info != protoRes.definition)
                        return false;

                    if (partRes.amount != protoRes.amount)
                        return false;

                    if (partRes.maxAmount != protoRes.maxAmount)
                        return false;

                    if (partRes.flowState != protoRes.flowState)
                        return false;
                }
            }

            int partId = part.GetInstanceID();
            int moduleCount = part.Modules.Count;

            if (partId != lastEqualityCheckPartId || lastEqualityCheckTime != Time.fixedTime || lastCheckedPartModuleNodes.Count != moduleCount)
            {
                lastCheckedPartModuleNodes.Clear();
                lastEqualityCheckPartId = partId;
                lastEqualityCheckTime = Time.fixedTime;
                for (int i = 0; i < moduleCount; i++)
                {
                    ConfigNode moduleNode = new ConfigNode("MODULE");
                    part.Modules[i].Save(moduleNode);
                    lastCheckedPartModuleNodes.Add(moduleNode);
                }
            }

            for (int i = 0; i < moduleCount; i++)
            {
                ProtoPartModuleSnapshot protoModule = protoPart.modules[i];

                if (part.Modules[i].moduleName != protoModule.moduleName)
                    return false;

                if (!ConfigNodeEquals(lastCheckedPartModuleNodes[i], protoModule.moduleValues))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Check if two ConfigNode are identical
        /// </summary>
        public static bool ConfigNodeEquals(ConfigNode nodeA, ConfigNode nodeB)
        {
            if (nodeA.name != nodeB.name)
                return false;

            if (nodeA.values.Count != nodeB.values.Count)
                return false;

            if (nodeA.nodes.Count != nodeB.nodes.Count)
                return false;

            for (int i = 0; i < nodeA.values.Count; i++)
            {
                ConfigNode.Value valueA = nodeA.values[i];
                ConfigNode.Value valueB = nodeB.values[i];

                if (valueA.name != valueB.name)
                    return false;

                if (valueA.value != valueB.value)
                    return false;
            }

            for (int i = 0; i < nodeA.nodes.Count; i++)
            {
                if (!ConfigNodeEquals(nodeA.nodes[i], nodeB.nodes[i]))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Get a part prefab bounds volume in liters. 
        /// While you can call this for instantiated parts, the part must be world-axis aligned for this method to work reliably.
        /// </summary>
        public static float GetPrefabPackedVolume(Part partPrefab)
        {
            float renderersVolume = -1f;
            float collidersVolume = -1f;

            Renderer[] renderers = partPrefab.transform.GetComponentsInChildren<Renderer>(false);

            if (renderers.Length > 0)
            {
                Bounds bounds = default;
                bounds.center = partPrefab.transform.position;

                foreach (Renderer renderer in renderers)
                {
                    if (!(renderer is MeshRenderer || renderer is SkinnedMeshRenderer))
                        continue;

                    if (renderer.tag != "Untagged" || renderer.gameObject.layer != 0)
                        continue;

                    bounds.Encapsulate(renderer.bounds);
                }

                Vector3 renderersSize = bounds.size;
                renderersVolume = Mathf.Ceil(renderersSize.x * renderersSize.y * renderersSize.z * 1000f);
            }

            Collider[] colliders = partPrefab.transform.GetComponentsInChildren<Collider>(false);

            if (colliders.Length > 0)
            {
                Bounds bounds = default;
                bounds.center = partPrefab.transform.position;

                foreach (Collider collider in colliders)
                {
                    if (collider.tag != "Untagged" || collider.gameObject.layer != 0)
                        continue;

                    bounds.Encapsulate(collider.bounds);
                }

                Vector3 collidersSize = bounds.size;
                collidersVolume = Mathf.Ceil(collidersSize.x * collidersSize.y * collidersSize.z * 1000f);
            }

            // colliders volume will usually be slightly (5-20%) lower than renderers volume
            // the default choice is the colliders volume, because :
            // - it is more representative of the volume for a "packed" part
            // - the results are more in line with the stock config-defined volumes
            // - renderers volume is less reliable overall, as many modules tend to disable them 
            //   once the part is instantiated (ex : fairing interstages, shrouds...)
            // However, in case the colliders volume is higher than the renderers volume, there 
            // is very likely some rogue colliders messing with us, so the renderers volume
            // is more likely to be accurate.
            float volume;
            if (renderersVolume > 0f && collidersVolume > renderersVolume)
                volume = renderersVolume;
            else if (collidersVolume > 0f)
                volume = collidersVolume;
            else
                volume = renderersVolume;

            if (volume < 100f)
                return volume;

            if (volume < 1000f)
                return (float)(Math.Round(volume / 5.0) * 5.0);

            if (volume < 5000f)
                return (float)(Math.Round(volume / 50.0) * 50.0);

            return (float)(Math.Round(volume / 100.0) * 100.0);

        }
    }
}
