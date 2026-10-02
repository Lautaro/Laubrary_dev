using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SaveGame;

namespace Laubrary.Demos
{
    /// <summary>
    /// Represents one SaveFile row in the list. On first Setup it destroys design-time
    /// snapshot clones, keeping only the first child of snapshotContainer as the template.
    /// </summary>
    public class SaveListItem : MonoBehaviour
    {
        [Header("References")]
        public TextMeshProUGUI titleText;
        public TextMeshProUGUI infoText;

        // Resolved at runtime via transform.Find — not serialized to avoid stale scene refs.
        private Transform snapshotContainer;

        private Button _button;
        private GameObject _snapshotTemplate;
        private SaveFile _saveData;
        private SaveManager _saveManager;
        private Action<string, object> _onSelected;
        private readonly List<SnapshotListItem> _snapshotItems = new();

        private const string ContainerName = "SnapshotContainer";

        /// <summary>Populates the row and its child snapshot list.</summary>
        public void Setup(SaveFile saveData, SaveManager saveManager,
                          Action<string, object> onSelected, bool isSelected)
        {
            _saveData    = saveData;
            _saveManager = saveManager;
            _onSelected  = onSelected;

            snapshotContainer = transform.Find(ContainerName);

            if (_button == null) _button = GetComponent<Button>();
            if (_button != null)
            {
                _button.onClick.RemoveAllListeners();
                _button.onClick.AddListener(() => _onSelected?.Invoke("save", _saveData));
            }

            if (titleText != null) titleText.text = saveData.Name;
            if (infoText  != null) infoText.text  =
                $"{saveData.SnapshotCount} snapshot(s)  •  {saveData.LastModified:g}";

            if (snapshotContainer != null)
                snapshotContainer.gameObject.SetActive(isSelected);

            if (isSelected)
            {
                EnsureSnapshotTemplate();
                PopulateSnapshots();
            }
        }

        /// <summary>
        /// Finds the snapshot template: the single inactive child that SaveGameDemo
        /// left in snapshotContainer during Start(). No destruction happens here.
        /// </summary>
        void EnsureSnapshotTemplate()
        {
            if (snapshotContainer == null || _snapshotTemplate != null) return;

            foreach (Transform child in snapshotContainer)
            {
                _snapshotTemplate = child.gameObject;
                break; // only one child remains after SaveGameDemo pre-cleaned the template
            }
        }

        void PopulateSnapshots()
        {
            if (snapshotContainer == null || _snapshotTemplate == null) return;

            foreach (var item in _snapshotItems)
                if (item != null) Destroy(item.gameObject);
            _snapshotItems.Clear();

            if (_saveData.Snapshots == null) return;

            foreach (var snapshot in _saveData.Snapshots)
            {
                GameObject row = Instantiate(_snapshotTemplate, snapshotContainer);
                row.SetActive(true);

                SnapshotListItem script = row.GetComponent<SnapshotListItem>();
                if (script != null)
                {
                    script.Setup(snapshot, _saveData.Identifier, _saveManager, _onSelected);
                    _snapshotItems.Add(script);
                }
            }
        }

        /// <summary>Highlights the given snapshot and clears all others.</summary>
        public void SetSnapshotSelected(string snapshotId)
        {
            foreach (var item in _snapshotItems)
                item.SetSelected(item.GetSnapshotId() == snapshotId);
        }
    }
}
