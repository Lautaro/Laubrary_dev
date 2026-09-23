// VisionMask — the per-PIXEL vision test. Every fragment asks "is MY world position inside a vision
// cone?" and gets a yes/no for that pixel alone. Nothing here knows about objects, bounds or overlap
// fractions: a sprite half inside a cone is half drawn because half of its fragments answer yes.
//
// Driven entirely by global shader parameters published by Laubrary.VisionMask.VisionMask (C#), so any
// material that includes this file sees the same cones. Generalises the City's LatticeVision.hlsl
// (one cone, lattice-bound) to up to VISION_MASK_MAX_CONES cones on a plain XY or XZ plane, each with
// an optional occlusion shadow map (one row per cone, reach per ray).
#ifndef LAUBRARY_VISION_MASK_INCLUDED
#define LAUBRARY_VISION_MASK_INCLUDED

#define VISION_MASK_MAX_CONES 8

float  _VisionMaskCount;    // live cones, 0..8
float  _VisionMaskPlane;    // 0 = XY (2D games), 1 = XZ (top-down 3D)
float  _VisionMaskBypass;   // 1 = everything visible (debug X-ray)
float4 _VisionMaskA[VISION_MASK_MAX_CONES];   // xy = eye (plane coords), zw = unit facing (plane coords)
float4 _VisionMaskB[VISION_MASK_MAX_CONES];   // x = cos(half angle), y = range, z = omni radius, w = half angle (rad)
float4 _VisionMaskC[VISION_MASK_MAX_CONES];   // x = occluded (0/1), y = rays in this cone's shadow row
TEXTURE2D(_VisionMaskShadow);                 // RFloat, width = max rays, row i = cone i: reach along each ray

float2 VisionMaskPlaneCoords(float3 world)
{
    return _VisionMaskPlane < 0.5 ? world.xy : world.xz;
}

// One cone's verdict on one plane point.
bool VisionMaskInCone(int i, float2 p)
{
    float4 a = _VisionMaskA[i];
    float4 b = _VisionMaskB[i];
    float2 d = p - a.xy;
    float r2 = dot(d, d);

    // Omni disc: seen in every direction and through occluders (a body glow, like the fog asset's
    // UnobscuredRadius and the City's omni disc).
    if (r2 <= b.z * b.z) return true;
    if (r2 > b.y * b.y || r2 <= 1e-10) return false;

    float2 dir = d * rsqrt(r2);
    float2 f = a.zw;
    if (dot(dir, f) < b.x) return false;          // outside the cone's angle

    float4 c = _VisionMaskC[i];
    if (c.x < 0.5) return true;                   // no occlusion: inside angle + range is enough

    // Occluded: the ray this pixel lies on stops at the first occluder; look its reach up.
    float ang = atan2(f.x * dir.y - f.y * dir.x, dot(f, dir));   // signed, CCW positive
    float u = (ang / max(b.w, 1e-6) + 1.0) * 0.5;                // 0..1 across the cone
    int rays = (int)c.y;
    int col = clamp((int)floor(u * rays), 0, rays - 1);
    float reach = LOAD_TEXTURE2D(_VisionMaskShadow, int2(col, i)).r;
    return r2 <= reach * reach;
}

// 1 when the world point is inside ANY live cone, else 0.
float VisionMaskVisibility(float3 world)
{
    if (_VisionMaskBypass > 0.5) return 1.0;
    float2 p = VisionMaskPlaneCoords(world);
    int n = (int)_VisionMaskCount;
    [loop] for (int i = 0; i < VISION_MASK_MAX_CONES; i++)
    {
        if (i >= n) break;
        if (VisionMaskInCone(i, p)) return 1.0;
    }
    return 0.0;
}

#endif
