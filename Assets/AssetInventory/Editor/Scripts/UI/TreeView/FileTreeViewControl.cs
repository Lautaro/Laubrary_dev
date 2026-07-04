using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
#pragma warning disable CS0618 // Type or member is obsolete
#if UNITY_6000_2_OR_NEWER
using BaseTreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;
#else
using BaseTreeViewState = UnityEditor.IMGUI.Controls.TreeViewState;
#endif

namespace AssetInventory
{
    internal class FileTreeViewControl : TreeViewWithTreeModel<FileTreeElement>
    {
        public TreeModel<FileTreeElement> Model => TreeModel;

        public FileTreeViewControl(BaseTreeViewState state, TreeModel<FileTreeElement> model) : base(state, model)
        {
            showAlternatingRowBackgrounds = true;
            showBorder = true;
            extraSpaceBeforeIconAndLabel = 20f; // Space for checkbox
            Reload();
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            TreeViewItem<FileTreeElement> item = (TreeViewItem<FileTreeElement>)args.item;
            FileTreeElement element = item.Data;

            Rect cellRect = args.rowRect;
            
            // Indentation
            float indent = GetContentIndent(item);
            
            // Checkbox
            Rect toggleRect = cellRect;
            toggleRect.x += indent;
            toggleRect.width = 20;

            EditorGUI.BeginDisabledGroup(element.IsAutoExcluded);
            bool displaySelected = element.IsAutoExcluded ? false : element.IsSelected;
            bool newSelected = EditorGUI.Toggle(toggleRect, displaySelected);
            if (!element.IsAutoExcluded && newSelected != element.IsSelected)
            {
                SetSelected(element, newSelected);
            }
            EditorGUI.EndDisabledGroup();

            // Icon & Label
            Rect contentRect = cellRect;
            contentRect.x += indent + 20;
            contentRect.width -= (indent + 20);

            Texture icon = element.IsFolder ? EditorGUIUtility.IconContent("Folder Icon").image : AssetDatabase.GetCachedIcon(element.Path);
            
            // Warning Icon
            if (element.Usages != null && element.Usages.Count > 0)
            {
                Rect warnRect = contentRect;
                warnRect.x += contentRect.width - 20;
                warnRect.width = 20;
                Texture warnIcon = EditorGUIUtility.IconContent("console.warnicon.sml").image;
                GUI.DrawTexture(warnRect, warnIcon, ScaleMode.ScaleToFit);
                contentRect.width -= 20;
                
                // Tooltip for warning
                if (warnRect.Contains(Event.current.mousePosition))
                {
                    GUI.Label(warnRect, new GUIContent("", "Used by:\n" + string.Join("\n", element.Usages)));
                }
            }

            GUIContent content = new GUIContent(element.TreeName, icon);
            EditorGUI.LabelField(contentRect, content);
        }

        private void SetSelected(FileTreeElement element, bool selected)
        {
            FileTreeSelection.SetSelected(element, selected);
        }
    }

    public static class FileTreeSelection
    {
        public static void SetSelected(FileTreeElement element, bool selected)
        {
            element.IsSelected = selected;

            if (element.HasChildren)
            {
                foreach (TreeElement child in element.Children)
                {
                    if (child is FileTreeElement fileChild)
                    {
                        SetSelected(fileChild, selected);
                    }
                }
            }
        }

        public static void SelectAll(TreeModel<FileTreeElement> model)
        {
            foreach (FileTreeElement element in model.GetData())
            {
                if (element.Depth >= 0) element.IsSelected = true;
            }
        }

        public static void DeselectConflicting(TreeModel<FileTreeElement> model)
        {
            foreach (FileTreeElement element in model.GetData())
            {
                if (element.Depth >= 0 && element.Usages != null && element.Usages.Count > 0)
                {
                    element.IsSelected = false;
                }
            }
        }

        public static bool HasConflicting(TreeModel<FileTreeElement> model)
        {
            foreach (FileTreeElement element in model.GetData())
            {
                if (element.Depth >= 0 && element.Usages != null && element.Usages.Count > 0) return true;
            }
            return false;
        }
    }
}

