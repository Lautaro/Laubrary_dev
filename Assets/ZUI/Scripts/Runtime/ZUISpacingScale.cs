// ZUISpacingScale.cs
using System;

/// <summary>
/// A named multiplier for ZUI spacing calls.
/// e.g. name="EqToolbar", scale=0.25 → ZUI.VerticalSpace("EqToolbar") = verticalSpacing × 0.25
/// </summary>
[Serializable]
public class ZUISpacingScale
{
    public string name  = "New Scale";
    public float  scale = 1f;
}
