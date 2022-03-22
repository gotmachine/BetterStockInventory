# BetterStockInventory

This plugin overhaul the KSP 1.12 inventory system :
- Inventories now have an unlimited amount of slots
- All technically identical parts are stackable up to 10 parts per stack, excepted parts holding resources.
- Automatic patching of all parts so they can used in the stock system (including parts from mods) :
  - Part volume is computed from the model bounds if not defined in the part config.
  - All parts whose volume is less than 2000 L can be be stored in inventories.
  - Larger parts are manipulable in EVA construction.
  - Parts variants have a separate volume for each variant.
  - Parts having inventories can be cargo parts, as long as their inventory is empty.
- Overhauled inventory UI :
  - Amount of visible slots is tweakable.
  - Pagination system for navigating between visible slots.
  - Automatic reorganization button.
- Variable cargo volume API for mods doing mesh switching, part resizing or procedural parts.
- Should be 100% compatible with other mods using the stock inventory.
- Bugfixes for stock inventory-related issues.

### Download and installation

Compatible with **KSP 1.12.3 ONLY**

**Required** and **must be downloaded separately** : 

- **ModuleManager** : **[Download](https://ksp.sarbian.com/jenkins/job/ModuleManager/lastSuccessfulBuild/artifact/)** - [Forum post](https://forum.kerbalspaceprogram.com/index.php?/topic/50533-18x-110x-module-manager-414-july-7th-2020-locked-inside-edition/) - Available on [CKAN]
- **KSPCommunityFixes** : **[Download](https://github.com/KSPModdingLibs/KSPCommunityFixes/releases)** - [Homepage](https://github.com/KSPModdingLibs/KSPCommunityFixes/) - Available on [CKAN]
- **HarmonyKSP** : **[Download](https://github.com/KSPModdingLibs/HarmonyKSP/releases)** - [Homepage](https://github.com/KSPModdingLibs/HarmonyKSP/) - Available on [CKAN]

### License

MIT

### Changelog

##### 0.1.0
First alpha release - for testing only

[CKAN]: https://forum.kerbalspaceprogram.com/index.php?/topic/197082-ckan-the-comprehensive-kerbal-archive-network-v1304-hubble/
