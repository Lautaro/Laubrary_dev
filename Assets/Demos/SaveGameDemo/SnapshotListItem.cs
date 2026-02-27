using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SaveGame;

namespace Laubrary.Demos
{
    public class SnapshotListItem : MonoBehaviour
    {
        private const string MetaKeyPlayTime = "playTime";
        [Header("References")]
        public TextMeshProUGUI nameText;
        public TextMeshProUGUI infoText;
        public RawImage thumbnail;
        public Image background;

        private static readonly Color ColorSelected   = new(0.2f, 0.5f, 0.85f, 0.5f);
        private static readonly Color ColorUnselected = new(0.15f, 0.15f, 0.18f, 1f);

        private Button _button;
        private SnapshotMetadata _snapshotData;
        private string _saveIdentifier;
        private SaveManager _saveManager;
        private Action<string, object> _onSelected;

        /// <summary>Wires up the row with snapshot data and a selection callback.</summary>
        public void Setup(SnapshotMetadata snapshotData, string saveIdentifier,
                          SaveManager saveManager, Action<string, object> onSelected)
        {
            _snapshotData   = snapshotData;
            _saveIdentifier = saveIdentifier;
            _saveManager    = saveManager;
            _onSelected     = onSelected;

            if (_button == null) _button = GetComponent<Button>();
            if (_button != null)
            {
                _button.onClick.RemoveAllListeners();
                _button.onClick.AddListener(OnClicked);
            }

            if (nameText != null)
                nameText.text = string.IsNullOrEmpty(snapshotData.Name) ? "Unnamed" : snapshotData.Name;

            if (infoText != null)
            {
                int savedSeconds = snapshotData.MetadataProperties.GetInt(MetaKeyPlayTime);
                infoText.text = $"{snapshotData.SaveType}  •  {snapshotData.Timestamp:g}  •  {FormatTime(savedSeconds)}";
            }

            if (thumbnail != null)
            {
                Texture2D thumb = saveManager.GetSnapshotThumbnail(saveIdentifier, snapshotData.Identifier);
                if (thumb != null)
                {
                    thumbnail.texture = thumb;
                    thumbnail.gameObject.SetActive(true);
                }
                else
                {
                    thumbnail.gameObject.SetActive(false);
                }
            }

            if (background == null) background = GetComponent<Image>();
            SetSelected(false);
        }

        void OnClicked() => _onSelected?.Invoke("snapshot", _snapshotData);

        /// <summary>Toggles the selection highlight on this row.</summary>
        public void SetSelected(bool selected)
        {
            if (background != null)
                background.color = selected ? ColorSelected : ColorUnselected;
        }

        /// <summary>Returns this snapshot's unique identifier.</summary>
        public string GetSnapshotId() => _snapshotData?.Identifier;

        private static string FormatTime(int totalSeconds)
        {
            TimeSpan ts = TimeSpan.FromSeconds(totalSeconds);
            return ts.Hours > 0
                ? $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }
    }
}
