// IZuiBands — what the ZUI bands control (ZuiBandsControl, reached through ZuiReflect) needs from a band table:
// N hard colour bands over t in 0..1, each starting at its threshold and running to the next. The data type that
// owns the bands (PyrePlus's PlusBands) implements this so the toolkit can draw and edit it without knowing the
// owner's assembly — the same reason ZUIValue / ZuiFill live here rather than beside their consumers.
//
// Contract: thresholds ascend and band 0 starts at 0 unless FirstIsFloor (then the region below it is empty and
// threshold 0 is editable). SetThreshold keeps the order (the implementer clamps between the neighbours).
using UnityEngine;

public interface IZuiBands
{
    int Count { get; }
    int MinCount { get; }
    int MaxCount { get; }
    /// True when the region below the first threshold is empty (transparent) rather than band 0 — so the first
    /// marker is meaningful and draggable.
    bool FirstIsFloor { get; }
    float GetThreshold(int i);
    void SetThreshold(int i, float t);
    Color GetColor(int i);
    void SetColor(int i, Color c);
    /// Resample to n bands, keeping the existing thresholds and colours.
    void SetCount(int n);
    /// The band colour at t — the hard step lookup the renderer uses (alpha 0 below the floor when FirstIsFloor).
    Color Eval(float t);
}
