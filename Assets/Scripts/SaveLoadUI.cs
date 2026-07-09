using UnityEngine;
using UnityEngine.UI;

namespace PangeaSkirmish
{
    /// <summary>
    /// UI controller for save/load functionality.
    /// Attach to a Canvas with save/load buttons and slot list.
    /// </summary>
    public class SaveLoadUI : MonoBehaviour
    {
        [Header("UI References")]
        public Transform saveSlotContainer;
        public GameObject saveSlotPrefab;
        public Button saveButton;
        public Button loadButton;
        public Button deleteButton;
        public Button closeButton;

        [Header("Settings")]
        public int maxSlots = 3;

        private string selectedSlot;

        private void Start()
        {
            // Setup button listeners
            if (saveButton != null)
                saveButton.onClick.AddListener(OnSaveClicked);
            if (loadButton != null)
                loadButton.onClick.AddListener(OnLoadClicked);
            if (deleteButton != null)
                deleteButton.onClick.AddListener(OnDeleteClicked);
            if (closeButton != null)
                closeButton.onClick.AddListener(Close);

            // Disable buttons initially
            SetButtonsActive(false);

            // Populate save slots
            RefreshSaveSlots();
        }

        public void Open()
        {
            gameObject.SetActive(true);
            RefreshSaveSlots();
        }

        public void Close()
        {
            gameObject.SetActive(false);
            selectedSlot = null;
            SetButtonsActive(false);
        }

        private void RefreshSaveSlots()
        {
            // Clear existing slots
            foreach (Transform child in saveSlotContainer)
            {
                Destroy(child.gameObject);
            }

            // Get existing saves
            string[] saves = SaveSystem.ListSaves();

            // Create slot buttons
            for (int i = 0; i < maxSlots; i++)
            {
                string slotName = $"save_{i}";
                bool hasSave = System.Array.Exists(saves, s => s == slotName);

                var slotGo = Instantiate(saveSlotPrefab, saveSlotContainer);
                var slotUI = slotGo.GetComponent<SaveSlotUI>();

                if (slotUI != null)
                {
                    slotUI.Setup(slotName, hasSave, OnSlotSelected);
                }
            }
        }

        private void OnSlotSelected(string slotName)
        {
            selectedSlot = slotName;
            SetButtonsActive(true);
        }

        private void SetButtonsActive(bool active)
        {
            if (saveButton != null) saveButton.interactable = active;
            if (loadButton != null) loadButton.interactable = active;
            if (deleteButton != null) deleteButton.interactable = active;
        }

        private void OnSaveClicked()
        {
            if (string.IsNullOrEmpty(selectedSlot))
                return;

            var state = SaveSystem.CaptureGameState();
            if (SaveSystem.Save(state, selectedSlot))
            {
                Debug.Log($"Game saved to slot: {selectedSlot}");
                RefreshSaveSlots();
            }
        }

        private void OnLoadClicked()
        {
            if (string.IsNullOrEmpty(selectedSlot))
                return;

            var state = SaveSystem.Load(selectedSlot);
            if (state != null)
            {
                SaveSystem.RestoreGameState(state);
                Close();
            }
        }

        private void OnDeleteClicked()
        {
            if (string.IsNullOrEmpty(selectedSlot))
                return;

            if (SaveSystem.Delete(selectedSlot))
            {
                RefreshSaveSlots();
                selectedSlot = null;
                SetButtonsActive(false);
            }
        }
    }

    /// <summary>
    /// Individual save slot UI element.
    /// </summary>
    public class SaveSlotUI : MonoBehaviour
    {
        public Text slotNameText;
        public Text timestampText;
        public Button selectButton;

        private string slotName;
        private System.Action<string> onSelected;

        public void Setup(string slotName, bool hasSave, System.Action<string> onSelected)
        {
            this.slotName = slotName;
            this.onSelected = onSelected;

            if (slotNameText != null)
                slotNameText.text = slotName.Replace("_", " ").ToUpper();

            if (timestampText != null)
            {
                if (hasSave)
                {
                    string ts = SaveSystem.GetTimestamp(slotName);
                    timestampText.text = string.IsNullOrEmpty(ts) ? "Has save data" : ts;
                }
                else
                {
                    timestampText.text = "Empty slot";
                }
            }

            if (selectButton != null)
                selectButton.onClick.AddListener(OnSelect);
        }

        private void OnSelect()
        {
            onSelected?.Invoke(slotName);
        }
    }
}
