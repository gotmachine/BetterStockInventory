using System.Collections.Generic;
using UnityEngine;

namespace InventoryAPI
{
    /// <summary>
    /// Implement this interface on a PartModule to handle a variable cargo module.
    /// </summary>
    public interface IVariablePackedVolumeModule
    {
        /// <summary>
        /// Must return true if that specific part instance can have multiple cargo volumes.
        /// If this is set to false, an auto-generated single packedVolume will be assigned to that part,
        /// and the CurrentPackedVolume() method won't be used.
        /// Note that this called on the part prefab, after the part compilation, changing that value on
        /// part instances has no effect. This is purely for optimization purposes, to prevent useless processing.
        /// If your module is using multiple volumes most of the time, you can just always return true
        /// </summary>
        bool InterfaceIsActive { get; }

        /// <summary>
        /// Must return the volume in liters, or -1f if the part can't be stored in an inventory in its current state.
        /// This will be called on running instances, both in the editors and in flight.
        /// Note that when that method is called, the part model will be in an invalid state due to how stock processes
        /// about-to-be-stored parts. So you can't analyze the model dynamically and you must either : <br/>
        /// - Use pre-configured values for each variation <br/>
        /// - Generate volumes during prefab compilation, and save them somewhere <br/>
        /// - Rely on scaling formulas
        /// To pre-generate a volume from the part prefab, you can use the InventoryAPI.GetPrefabPackedVolume() method 
        /// after setting the part model in the desired state.
        /// </summary>
        /// <param name="defaultVolume">The config-defined cargo volume for this part. A value of -1f mean the part is configured to be non-storable</param>
        /// <param name="isVariantVolume">If true, defaultVolume is specific to the current variant</param>
        /// <returns></returns>
        float CurrentPackedVolume(float configVolume, bool isVariantVolume);

        /// <summary>
        /// Optional, return null or empty to ignore. Otherwise, will replace the default content of the
        /// ModuleCargoPart part info widget (IModuleInfo.GetInfo()).
        /// This is a good place to put a list of volumes for each variants, or to give the player
        /// some information about how the cargo part volume will be determined.
        /// </summary>
        string CargoModuleInfo();
    }

    /// <summary>
    /// Implement this interface on a PartModule to replace or add info widgets shown in a cargo part right-click popup.
    /// All members will be called only on the part prefab instance, so you can't rely on the module/part instance state.
    /// To create your widgets, you must parse the provided cargo part / protomodule
    /// </summary>
    public interface ICargoModuleCustomInfo
    {
        /// <summary>
        /// If true, the default module widget as defined by the module IModuleInfo.GetInfo() implementation (if any) will not be shown.
        /// </summary>
        bool OverwriteDefaultWidget { get; }

        /// <summary>
        /// Return one or multiple widget(s) data to add to the cargo part tooltip
        /// </summary>
        IEnumerable<WidgetInfo> GetWidgets(StoredPart storedPart, ProtoPartModuleSnapshot protoModule);
    }

    /// <summary>
    /// A widget content
    /// </summary>
    public struct WidgetInfo
    {
        public string title;
        public string content;
        public Color color;

        /// <summary>
        /// Define a widget
        /// </summary>
        /// <param name="title">Top title. Keep it short</param>
        /// <param name="content">Widget content. Supports TextMeshPro rich text tags.</param>
        /// <param name="color">Optional background color for the widget</param>
        public WidgetInfo(string title, string content, Color color = default)
        {
            this.title = title;
            this.content = content;
            this.color = color;
        }
    }
}
