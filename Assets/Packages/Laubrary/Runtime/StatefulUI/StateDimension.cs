using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Lau_StatefulUI
{
    [Serializable]
    public class GameObjectToggle
    {
        public GameObject gameObject;
        public bool enabled = true;
    }

    public enum StateDimension
    {
        Interaction,
        Navigated,
        Toggled,
        Focused
    }

    public enum InteractionState
    {
        Normal = 0,
        Hover = 1,
        Selected = 2,
        Clicked = 3,
        Disabled = 4
    }

    public enum RadioGroupBehavior
    {
        SetToggled,
        SetSelected
    }

    [Flags]
    public enum StateModifierFlags
    {
        None = 0,
        Color = 1 << 0,
        Alpha = 1 << 1,
        ColorValue = 1 << 2,
        Saturation = 1 << 3,
        Position = 1 << 4,
        Scale = 1 << 5,
        Rotation = 1 << 6,
        Sprite = 1 << 7,
        GameObjects = 1 << 8,
        Duration = 1 << 9
    }

    [Flags]
    public enum InteractionStateOverrides
    {
        None = 0,
        Normal = 1 << 0,
        Hover = 1 << 1,
        Selected = 1 << 2,
        Clicked = 1 << 3,
        Disabled = 1 << 4
    }

    [Flags]
    public enum BooleanStateOverrides
    {
        None = 0,
        Navigated = 1 << 0,
        Toggled = 1 << 1,
        Focused = 1 << 2
    }

    [Serializable]
    public class InteractionStateConfig
    {
        public bool custom;
        public StateModifierFlags enabledModifiers;
        public UnityEngine.Color color = UnityEngine.Color.white;
        public float alpha = 1f;
        public bool alphaIsAdditive;
        public float colorValue = 1f;
        public bool colorValueIsAdditive;
        public float saturation = 1f;
        public bool saturationIsAdditive;
        public UnityEngine.Vector2 position = UnityEngine.Vector2.zero;
        public bool positionIsAdditive;
        public float scale = 1f;
        public bool scaleIsAdditive;
        public float rotation = 0f;
        public bool rotationIsAdditive;
        public UnityEngine.Sprite sprite;
        public List<GameObjectToggle> gameObjects = new List<GameObjectToggle>();
        public float duration = 0.1f;
    }

    [Serializable]
    public class BooleanStateConfig
    {
        public bool custom;
        public StateModifierFlags enabledModifiers;
        public UnityEngine.Color color = UnityEngine.Color.white;
        public float alpha = 1f;
        public bool alphaIsAdditive;
        public float colorValue = 1f;
        public bool colorValueIsAdditive;
        public float saturation = 1f;
        public bool saturationIsAdditive;
        public UnityEngine.Vector2 position = UnityEngine.Vector2.zero;
        public bool positionIsAdditive;
        public float scale = 1f;
        public bool scaleIsAdditive;
        public float rotation = 0f;
        public bool rotationIsAdditive;
        public UnityEngine.Sprite sprite;
        public List<GameObjectToggle> gameObjects = new List<GameObjectToggle>();
        public float duration = 0.1f;
    }
}
