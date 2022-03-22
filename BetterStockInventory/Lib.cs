using System;
using System.Collections.Generic;

namespace BetterStockInventory
{
    public static class Lib
	{
        public static BaseFieldList<BaseField, KSPField>.ReflectedData GetReflectedAttributes(Type partModuleType)
		{
			try
            {
                return BaseFieldList.GetReflectedAttributes(partModuleType, false);
			}
			catch
			{
				return null;
			}
		}

		public static bool EditPartModuleKSPFieldAttributes(Type partModuleType, string fieldName, Action<KSPField> editAction)
		{
			BaseFieldList<BaseField, KSPField>.ReflectedData reflectedData = GetReflectedAttributes(partModuleType);

			if (reflectedData == null)
				return false;

			for (int i = 0; i < reflectedData.fields.Count; i++)
			{
				if (reflectedData.fields[i].Name == fieldName)
				{
					editAction.Invoke(reflectedData.fieldAttributes[i]);
					return true;
				}
			}

			return false;
		}


		private static ProtoPartSnapshot lastProtoPartPrefabSearch;
		private static bool lastProtoPartPrefabSearchIsSync;


		/// <summary>
		/// Find the PartModule instance in the Part prefab corresponding to a ProtoPartModuleSnapshot
		/// </summary>
		/// <param name="protoModuleIndex">
		/// The index of the ProtoPartModuleSnapshot in the protoPart.
		/// If the prefab isn't synchronized due to a configs change, the parameter is updated to the module index in the prefab
		/// </param>
		public static bool TryFindModulePrefab(ProtoPartSnapshot protoPart, ref int protoModuleIndex, out PartModule prefab)
		{
			if (protoPart == lastProtoPartPrefabSearch)
			{
				if (lastProtoPartPrefabSearchIsSync)
				{
					prefab = protoPart.partPrefab.Modules[protoModuleIndex];
					return true;
				}
			}
			else
			{
				lastProtoPartPrefabSearch = protoPart;
				lastProtoPartPrefabSearchIsSync = ArePrefabModulesInSync(protoPart.partPrefab, protoPart.modules);
				if (lastProtoPartPrefabSearchIsSync)
				{
					prefab = protoPart.partPrefab.Modules[protoModuleIndex];
					return true;
				}
			}

			prefab = null;
			int protoIndexInType = 0;
			ProtoPartModuleSnapshot module = protoPart.modules[protoModuleIndex];
			foreach (ProtoPartModuleSnapshot otherppms in protoPart.modules)
			{
				if (otherppms.moduleName == module.moduleName)
				{
					if (otherppms == module)
						break;

					protoIndexInType++;
				}
			}

			int prefabIndexInType = 0;
			for (int i = 0; i < protoPart.partPrefab.Modules.Count; i++)
			{
				if (protoPart.partPrefab.Modules[i].moduleName == module.moduleName)
				{
					if (prefabIndexInType == protoIndexInType)
					{
						prefab = protoPart.partPrefab.Modules[i];
						protoModuleIndex = i;
						break;
					}

					prefabIndexInType++;
				}
			}

			if (prefab == null)
			{
				return false;
			}

			return true;
		}

		private static bool ArePrefabModulesInSync(Part partPrefab, List<ProtoPartModuleSnapshot> protoPartModules)
		{
			if (partPrefab.Modules.Count != protoPartModules.Count)
				return false;

			for (int i = 0; i < protoPartModules.Count; i++)
			{
				if (partPrefab.Modules[i].moduleName != protoPartModules[i].moduleName)
				{
					return false;
				}
			}

			return true;
		}

	}
}
