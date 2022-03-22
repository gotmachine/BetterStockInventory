using System.Collections;
using System.Collections.Generic;
using KSP.UI.Screens;
using KSP.UI.Screens.Editor;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterStockInventory
{
    public class UnlimitedInventorySlotsUI : MonoBehaviour
    {
        private class InventorySettings
        {
            public int visibleRows;
            public int currentPageIndex;

            public InventorySettings(int visibleRows, int currentPageIndex)
            {
                this.visibleRows = visibleRows;
                this.currentPageIndex = currentPageIndex;
            }
        }

        private static Dictionary<int, InventorySettings> settings = new Dictionary<int, InventorySettings>();

        private UIPartActionInventory inventory;

        private int moduleInstanceId;
        private InventorySettings inventorySettings;

        private int pageCount;

        private int _visibleRows;
        private int VisibleRows
        {
            get => _visibleRows;
            set
            {
                _visibleRows = value;
                inventorySettings.visibleRows = value;
            }
        }

        private int _currentPageIndex;
        private int CurrentPageIndex
        {
            get => _currentPageIndex;
            set
            {
                _currentPageIndex = value;
                inventorySettings.currentPageIndex = value;
            }
        }

        private int SlotsPerPage => _visibleRows * 3;

        private TextMeshProUGUI currentPageText;
        private TextMeshProUGUI pageCountText;
        private Slider pageSlider;

        public void PreInitializeSlots(UIPartActionInventory inventory)
        {
            this.inventory = inventory;

            moduleInstanceId = inventory.inventoryPartModule.GetInstanceID();

            if (!inventory.inCargoPane && settings.TryGetValue(moduleInstanceId, out inventorySettings))
            {
                _visibleRows = inventorySettings.visibleRows;
                _currentPageIndex = inventorySettings.currentPageIndex;
            }
            else
            {
                _visibleRows = 1;
                _currentPageIndex = -1;
            }

            int slotCount = 0;
            if (inventory.inventoryPartModule.storedParts != null)
            {
                // get slot count up to the last occupied slot
                for (int i = 0; i < inventory.inventoryPartModule.storedParts.Count; i++)
                {
                    StoredPart storedPart = inventory.inventoryPartModule.storedParts.At(i);
                    if (storedPart.slotIndex + 1 > slotCount)
                    {
                        slotCount = storedPart.slotIndex + 1;
                    }
                }
            }

            // make sure there is at least one empty slot
            slotCount++;

            // if this is in the cargo UI (not a PAW), show all items
            if (inventory.inCargoPane)
                _visibleRows = (slotCount - 1) / 3 + 1;

            // add slots to fill last page
            slotCount = SlotCountToFillPages(slotCount);

            // compute page count
            pageCount = (slotCount - 1) / SlotsPerPage + 1;

            // show last page if not saved
            if (_currentPageIndex < 0 || pageCount >= _currentPageIndex)
                _currentPageIndex = pageCount - 1;

            // let the stock init code create the slots for us
            inventory.inventoryPartModule.InventorySlots = slotCount;
            inventory.fieldValue = slotCount;

            if (inventorySettings == null)
            {
                inventorySettings = new InventorySettings(_visibleRows, _currentPageIndex);
                settings[moduleInstanceId] = inventorySettings;
            }

            // Add our page / slot selection UI
            GameObject header = new GameObject("PageSelection");
            header.layer = 5;
            RectTransform transform = header.AddComponent<RectTransform>();
            LayoutElement layout = header.AddComponent<LayoutElement>();
            layout.minHeight = 18f;
            layout.flexibleWidth = 1f;
            transform.SetParent(inventory.contentTransform.parent);
            transform.SetAsFirstSibling();
            transform.position = Vector3.zero;
            transform.localPosition = Vector3.zero;

            GameObject resPriorityPrefab = UIPartActionController.Instance.resourcePriorityPrefab.gameObject;
            GameObject buttonPrefab = resPriorityPrefab.transform.GetChild(0).Find("BtnInc").gameObject;

            GameObject fit = Instantiate(buttonPrefab, transform);
            RectTransform fitTransform = (RectTransform)fit.transform;
            fitTransform.anchorMin = Vector2.zero;
            fitTransform.anchorMax = Vector2.zero;
            fitTransform.pivot = Vector2.zero;
            fitTransform.anchoredPosition = new Vector2(0f, 0f);
            fitTransform.sizeDelta = new Vector2(20f, 18f);
            Button fitButton = fit.GetComponent<Button>();
            fitButton.onClick.AddListener(Fit);
            TextMeshProUGUI fitText = fit.GetComponentInChildren<TextMeshProUGUI>();
            fitText.overflowMode = TextOverflowModes.Overflow;
            fitText.enableAutoSizing = false;
            fitText.fontSize = 12f;
            fitText.fontStyle = FontStyles.Bold;
            fitText.text = "Fit";

            GameObject chooser = Instantiate(UIPartActionController.Instance.resourceItemEditorPrefab.sliderContainer, transform);
            RectTransform chooserTransform = (RectTransform)chooser.transform;
            chooserTransform.pivot = Vector2.zero;
            chooserTransform.anchoredPosition = new Vector2(25f, 0f);
            chooserTransform.sizeDelta = new Vector2(110f, 18f);
            pageSlider = chooser.GetComponent<Slider>();
            TextMeshProUGUI label = chooser.transform.Find("Name").GetComponent<TextMeshProUGUI>();
            currentPageText = chooser.transform.Find("Amount").GetComponent<TextMeshProUGUI>();
            pageCountText = chooser.transform.Find("Total").GetComponent<TextMeshProUGUI>();

            pageSlider.wholeNumbers = true;
            pageSlider.minValue = 1f;
            pageSlider.onValueChanged.AddListener(OnPageSelected);

            label.text = "Page";
            UpdatePageSlider();

            GameObject maxRow = Instantiate(buttonPrefab, transform);
            RectTransform maxRowTransform = (RectTransform)maxRow.transform;
            maxRowTransform.anchoredPosition = new Vector2(-20f, 0f);
            maxRowTransform.sizeDelta = new Vector2(20f, 18f);
            Button maxRowButton = maxRow.GetComponent<Button>();
            maxRowButton.onClick.AddListener(ShowMaxRows);
            TextMeshProUGUI maxRowText = maxRow.GetComponentInChildren<TextMeshProUGUI>();
            maxRowText.overflowMode = TextOverflowModes.Overflow;
            maxRowText.enableAutoSizing = false;
            maxRowText.fontSize = 14f;
            maxRowText.fontStyle = FontStyles.Bold;
            maxRowText.text = "++";

            GameObject addRow = Instantiate(buttonPrefab, transform);
            RectTransform addRowTransform = (RectTransform)addRow.transform;
            addRowTransform.anchoredPosition = new Vector2(-42f, 0f);
            addRowTransform.sizeDelta = new Vector2(20f, 18f);
            Button addRowButton = addRow.GetComponent<Button>();
            addRowButton.onClick.AddListener(AddRow);
            TextMeshProUGUI addRowText = addRow.GetComponentInChildren<TextMeshProUGUI>();
            addRowText.overflowMode = TextOverflowModes.Overflow;
            addRowText.enableAutoSizing = false;
            addRowText.fontSize = 14f;
            addRowText.fontStyle = FontStyles.Bold;
            addRowText.text = "+";

            GameObject removeRow = Instantiate(buttonPrefab.gameObject, transform);
            RectTransform removeRowTransform = (RectTransform)removeRow.transform;
            removeRowTransform.anchoredPosition = new Vector2(-64f, 0f);
            removeRowTransform.sizeDelta = new Vector2(20f, 18f);
            Button removeRowButton = removeRow.GetComponent<Button>();
            removeRowButton.onClick.AddListener(RemoveRow);
            TextMeshProUGUI removeRowText = removeRow.GetComponentInChildren<TextMeshProUGUI>();
            removeRowText.overflowMode = TextOverflowModes.Overflow;
            removeRowText.enableAutoSizing = false;
            removeRowText.fontSize = 14f;
            removeRowText.fontStyle = FontStyles.Bold;
            removeRowText.text = "-";

            GameObject minRow = Instantiate(buttonPrefab, transform);
            RectTransform minRowTransform = (RectTransform)minRow.transform;
            minRowTransform.anchoredPosition = new Vector2(-86f, 0f);
            minRowTransform.sizeDelta = new Vector2(20f, 18f);
            Button minRowButton = minRow.GetComponent<Button>();
            minRowButton.onClick.AddListener(ShowSingleRow);
            TextMeshProUGUI minRowText = minRow.GetComponentInChildren<TextMeshProUGUI>();
            minRowText.overflowMode = TextOverflowModes.Overflow;
            minRowText.enableAutoSizing = false;
            minRowText.fontSize = 14f;
            minRowText.fontStyle = FontStyles.Bold;
            minRowText.text = "--";
        }

        public void PostInitializeSlots()
        {
            int iconsPerPage = SlotsPerPage;
            int firstActiveIndex = CurrentPageIndex * iconsPerPage;
            int firstInactiveIndex = firstActiveIndex + iconsPerPage;
            for (int i = 0; i < inventory.slotPartIcon.Count; i++)
            {
                if (i >= firstActiveIndex && i < firstInactiveIndex)
                    inventory.slotPartIcon[i].gameObject.SetActive(true);
                else
                    inventory.slotPartIcon[i].gameObject.SetActive(false);
            }
        }

        public static void ClearSettings()
        {
            settings.Clear();
        }

        public static void OnInventoryChanged(UIPartActionInventory inventory, ModuleInventoryPart module)
        {
            UnlimitedInventorySlotsUI betterInventory = inventory.GetComponent<UnlimitedInventorySlotsUI>();

            if (betterInventory == null || betterInventory.inventory.inventoryPartModule != module)
                return;

            int slotCount = inventory.slotPartIcon.Count;

            // if last slot is filled, add a new row
            if (!inventory.slotPartIcon[slotCount - 1].isEmptySlot)
            {
                betterInventory.AddRow();
            }
            // else remove empty pages if there is space in last slot from previous page
            else if (betterInventory.pageCount > 1)
            {
                int slotsPerPage = betterInventory.SlotsPerPage;
                int lastEmptySlotIndex = slotCount - 1;
                while (lastEmptySlotIndex >= 0 && inventory.slotPartIcon[lastEmptySlotIndex].isEmptySlot)
                {
                    lastEmptySlotIndex--;
                }

                int newPageCount = (lastEmptySlotIndex + 1) / slotsPerPage + 1;
                if (newPageCount == betterInventory.pageCount)
                    return;

                int newSlotCount = newPageCount * slotsPerPage;

                while (slotCount > newSlotCount)
                {
                    betterInventory.RemoveLastSlot();
                    slotCount--;
                }

                betterInventory.pageCount = newPageCount;

                if (betterInventory.CurrentPageIndex >= newPageCount)
                {
                    betterInventory.CurrentPageIndex = newPageCount - 1;
                    int firstSlotToEnable = betterInventory.CurrentPageIndex * slotsPerPage;
                    int firstDisabledSlot = firstSlotToEnable + slotsPerPage;
                    for (int i = firstSlotToEnable; i < firstDisabledSlot; i++)
                    {
                        inventory.slotPartIcon[i].gameObject.SetActive(true);
                    }
                }

                betterInventory.UpdatePageSlider();
            }
        }

        private void OnPageSelected(float page)
        {
            int selectedPageIndex = (int)page - 1;

            if (selectedPageIndex == CurrentPageIndex)
                return;

            int slotCount = inventory.slotPartIcon.Count;
            int slotsPerPage = SlotsPerPage;

            int firstIconToEnable = selectedPageIndex * slotsPerPage;
            for (int i = firstIconToEnable; i < firstIconToEnable + slotsPerPage && i < slotCount; i++)
            {
                inventory.slotPartIcon[i].gameObject.SetActive(true);
            }

            int firstIconToDisable = CurrentPageIndex * slotsPerPage;
            for (int i = firstIconToDisable; i < firstIconToDisable + slotsPerPage && i < slotCount; i++)
            {
                inventory.slotPartIcon[i].gameObject.SetActive(false);
            }

            CurrentPageIndex = selectedPageIndex;
            UpdatePageSlider();
        }

        private void AddRow()
        {
            VisibleRows++;
            OnVisibleRowsChanged();
        }

        private void RemoveRow()
        {
            if (VisibleRows == 1)
                return;

            VisibleRows--;
            OnVisibleRowsChanged();
        }

        private void OnVisibleRowsChanged()
        {
            UpdatePageCountAndTrimSlots();

            int slotCount = inventory.slotPartIcon.Count;
            int slotsPerPage = SlotsPerPage;

            int lastActiveIndex;
            for (lastActiveIndex = slotCount - 1; lastActiveIndex >= 0; lastActiveIndex--)
            {
                if (inventory.slotPartIcon[lastActiveIndex].gameObject.activeSelf)
                    break;
            }

            CurrentPageIndex = (lastActiveIndex - 1) / slotsPerPage;
            int firstActiveIndex = CurrentPageIndex * slotsPerPage;

            int firstInactiveIndex = firstActiveIndex + slotsPerPage;
            for (int i = 0; i < slotCount; i++)
            {
                if (i >= firstActiveIndex && i < firstInactiveIndex)
                    inventory.slotPartIcon[i].gameObject.SetActive(true);
                else
                    inventory.slotPartIcon[i].gameObject.SetActive(false);
            }

            UpdatePageSlider();
            UpdateLayout();
            SyncOtherInventories();
        }

        private void Fit()
        {
            if (inventory.inventoryPartModule.storedParts == null)
                return;

            DictionaryValueList<int, StoredPart> inventoryParts = inventory.inventoryPartModule.storedParts;
            List<StoredPart> storedParts = new List<StoredPart>(inventoryParts.Values);
            storedParts.Sort((x, y) => x.partName.CompareTo(y.partName));
            inventoryParts.Clear();
            for (int i = 0; i < storedParts.Count; i++)
            {
                StoredPart storedPart = storedParts[i];
                storedPart.slotIndex = i;
                inventoryParts[i] = storedPart;
            }

            GameEvents.onModuleInventoryChanged.Fire(inventory.inventoryPartModule);
            ShowMaxRows();
        }

        private void ShowSingleRow()
        {
            VisibleRows = 1;
            CurrentPageIndex = 0;

            UpdatePageCountAndTrimSlots();

            int slotCount = inventory.slotPartIcon.Count;
            int slotsPerPage = SlotsPerPage;

            for (int i = 0; i < slotCount && i < slotsPerPage; i++)
            {
                inventory.slotPartIcon[i].gameObject.SetActive(true);
            }

            for (int i = slotsPerPage; i < slotCount; i++)
            {
                inventory.slotPartIcon[i].gameObject.SetActive(false);
            }

            UpdatePageSlider();
            UpdateLayout();
            SyncOtherInventories();
        }

        private void ShowMaxRows()
        {
            int lastFilledSlotIndex = LastFilledSlotIndex();

            pageCount = 1;
            CurrentPageIndex = 0;
            VisibleRows = (lastFilledSlotIndex + 1) / 3 + 1;

            int currentSlotCount = inventory.slotPartIcon.Count;
            int newSlotCount = pageCount * SlotsPerPage;

            while (currentSlotCount > newSlotCount)
            {
                RemoveLastSlot();
                currentSlotCount--;
            }

            while (currentSlotCount < newSlotCount)
            {
                AddEmptySlot();
                currentSlotCount++;
            }

            foreach (EditorPartIcon editorPartIcon in inventory.slotPartIcon)
            {
                editorPartIcon.gameObject.SetActive(true);
            }

            UpdatePageSlider();
            UpdateLayout();
            SyncOtherInventories();
        }

        private void UpdatePageCountAndTrimSlots()
        {
            int lastFilledSlotIndex = LastFilledSlotIndex();
            int currentSlotCount = inventory.slotPartIcon.Count;
            int slotsPerPage = SlotsPerPage;

            pageCount = (lastFilledSlotIndex + 1) / slotsPerPage + 1;
            int newSlotCount = pageCount * slotsPerPage;

            while (currentSlotCount > newSlotCount)
            {
                RemoveLastSlot();
                currentSlotCount--;
            }

            while (currentSlotCount < newSlotCount)
            {
                AddEmptySlot();
                currentSlotCount++;
            }
        }

        private int LastFilledSlotIndex()
        {
            for (int i = inventory.slotPartIcon.Count - 1; i >= 0; i--)
            {
                if (!inventory.slotPartIcon[i].isEmptySlot)
                {
                    return i;
                }
            }

            return 0;
        }

        private int SlotCountToFillPages(int slotCount)
        {
            int slotsPerPage = SlotsPerPage;
            return ((slotCount + slotsPerPage - 1) / slotsPerPage) * slotsPerPage;
        }

        private void AddEmptySlot()
        {
            GameObject gameObject = Instantiate(inventory.slotPrefab, inventory.contentTransform);
            gameObject.transform.SetParent(inventory.contentTransform);
            gameObject.transform.localPosition = Vector3.zero;
            inventory.slotPartIcon.Add(gameObject.GetComponent<EditorPartIcon>());
            UIPartActionInventorySlot item = gameObject.AddComponent<UIPartActionInventorySlot>();
            Destroy(gameObject.GetComponent<PartListTooltipController>());
            gameObject.AddComponent<InventoryPartListTooltipController>().tooltipPrefab = UIPartActionControllerInventory.Instance.inventoryTooltipPrefab.GetComponent<InventoryPartListTooltip>();
            inventory.slotButton.Add(item);
            inventory.SpawnEmptySlot(inventory.slotPartIcon.Count - 1);
            if (HighLogic.LoadedSceneIsFlight)
            {
                PartListTooltipController component = gameObject.GetComponent<PartListTooltipController>();
                if (component != null)
                {
                    component.enabled = false;
                }
            }
        }

        private void RemoveLastSlot()
        {
            int lastSlotIndex = inventory.slotButton.Count - 1;
            inventory.slotPartIcon.RemoveAt(lastSlotIndex);
            inventory.slotButton[lastSlotIndex].gameObject.DestroyGameObject();
            inventory.slotButton.RemoveAt(lastSlotIndex);
        }

        private void UpdatePageSlider()
        {
            currentPageText.text = (CurrentPageIndex + 1).ToString();
            pageCountText.text = pageCount.ToString();
            pageSlider.maxValue = pageCount;
            pageSlider.SetValueWithoutNotify(CurrentPageIndex + 1);
        }

        private void UpdateLayout()
        {
            if (inventory.Field?.group != null
                && inventory.Window != null
                && inventory.Window.isActiveAndEnabled
                && inventory.Window.parameterGroups.TryGetValue(inventory.Field.group.name, out UIPartActionGroup group))
            {
                StartCoroutine(UpdateLayoutDeffered(group));
            }
        }

        private IEnumerator UpdateLayoutDeffered(UIPartActionGroup group)
        {
            yield return new WaitForEndOfFrame();
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)group.transform);
        }

        /// <summary>
        /// We need to sync the amount of instantiated slots between the PAW and side panel inventory UIs
        /// </summary>
        private void SyncOtherInventories()
        {
            ModuleInventoryPart module = inventory.inventoryPartModule;
            if (inventory.inCargoPane)
            {
                if (module.grid?.pawInventory != null)
                    SyncOtherInventory(module.grid.pawInventory, VisibleRows, pageCount, CurrentPageIndex);
            }
            else
            {
                if (InventoryPanelController.Instance != null && InventoryPanelController.Instance.IsOpen)
                {
                    foreach (InventoryPanelController.InventoryDisplayItem item in InventoryPanelController.Instance.displayedInventories)
                    {
                        if (item.inventoryModule == module)
                        {
                            SyncOtherInventory(item.uiInventory, VisibleRows, pageCount, CurrentPageIndex);
                            break;
                        }
                    }
                }
                else if (EVAConstructionModeController.Instance != null && EVAConstructionModeController.Instance.IsOpen)
                {
                    foreach (EVAConstructionModeController.InventoryDisplayItem item in EVAConstructionModeController.Instance.displayedInventories)
                    {
                        if (item.inventoryModule == module)
                        {
                            SyncOtherInventory(item.uiInventory, VisibleRows, pageCount, CurrentPageIndex);
                            break;
                        }
                    }
                }
            }
        }

        private static void SyncOtherInventory(UIPartActionInventory uiPartActionInventory, int visibleRows, int pageCount, int currentPageIndex)
        {
            UnlimitedInventorySlotsUI inventory = uiPartActionInventory.GetComponent<UnlimitedInventorySlotsUI>();

            if (inventory == null)
                return;

            int newSlotCount = pageCount * visibleRows * 3;

            while (uiPartActionInventory.slotPartIcon.Count > newSlotCount)
                inventory.RemoveLastSlot();

            while (uiPartActionInventory.slotPartIcon.Count < newSlotCount)
                inventory.AddEmptySlot();

            inventory.VisibleRows = visibleRows;
            inventory.pageCount = pageCount;

            inventory.OnPageSelected(currentPageIndex + 1);

            if (!uiPartActionInventory.inCargoPane)
                inventory.UpdateLayout();
        }
    }
}