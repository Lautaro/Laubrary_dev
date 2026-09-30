using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// <summary>An immutable snapshot of the sheets and explicit tool presentation classes effective at one UI root.</summary>
    public sealed class ZuiPresentationContext
    {
        const string ToolClassPrefix = "lau-tool-";
        readonly StyleSheet[] _styleSheets;
        readonly string[] _toolClasses;

        ZuiPresentationContext(StyleSheet[] styleSheets, string[] toolClasses)
        {
            _styleSheets = styleSheets;
            _toolClasses = toolClasses;
        }

        /// <summary>Captures ancestor stylesheets outer-to-inner and explicit <c>lau-tool-*</c> presentation classes.</summary>
        public static ZuiPresentationContext Capture(VisualElement owner, params string[] additionalRootClasses)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            var explicitClasses = new HashSet<string>(additionalRootClasses ?? Array.Empty<string>(), StringComparer.Ordinal);
            var ancestors = new List<VisualElement>();
            for (var current = owner; current != null; current = current.hierarchy.parent) ancestors.Add(current);
            ancestors.Reverse();

            var sheets = new List<StyleSheet>();
            var seenSheets = new HashSet<StyleSheet>();
            var classes = new List<string>();
            var seenClasses = new HashSet<string>();
            foreach (var root in ancestors)
            {
                for (int i = 0; i < root.styleSheets.count; i++)
                {
                    var sheet = root.styleSheets[i];
                    if (sheet != null && seenSheets.Add(sheet)) sheets.Add(sheet);
                }
                foreach (var className in root.GetClasses())
                    if ((className.StartsWith(ToolClassPrefix, StringComparison.Ordinal) || explicitClasses.Contains(className)) && seenClasses.Add(className)) classes.Add(className);
            }
            return new ZuiPresentationContext(sheets.ToArray(), classes.ToArray());
        }

        /// <summary>Applies this snapshot locally. It never observes later owner stylesheet or class changes.</summary>
        public void ApplyTo(VisualElement root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            Z.Attach(root);
            foreach (var sheet in _styleSheets)
                if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            foreach (var className in _toolClasses) root.AddToClassList(className);
            root.AddToClassList("zui-root");
        }
    }
}
