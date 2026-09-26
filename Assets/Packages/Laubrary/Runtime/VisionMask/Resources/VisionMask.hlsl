// VisionMask — the per-PIXEL vision test. Every fragment asks "how visible is MY world position?" and gets an
// answer for that pixel alone. Nothing here knows about objects, bounds or overlap fractions: a sprite half
// inside a cone is half drawn because half of its fragments answer yes.
//
// Driven entirely by global shader parameters published by Laubrary.VisionMask.VisionMask (C#), so any
// material that includes this file sees the same cones. Generalises the City's LatticeVision.hlsl
// (one cone, lattice-bound) to up to VISION_MASK_MAX_CONES cones on a plain XY or XZ plane, each with
// an optional occlusion shadow map (one row per cone, reach per ray).
//
// EDGE FADE (VisionCone.edgeFade = F world units, 2026-09-26). With F = 0 the answer is the hard yes/no it always
// was, bit for bit. With F > 0 a pixel OUTSIDE the cone is not simply dropped: it fades out with its true
// Euclidean distance to the cone's lit region (the sector and the omni disc), 1 at the edge -> 0 at F, along a
// smoothstep. Measured as a plane distance, never as an angle, so the band is equally wide at the angular edges,
// the range arc and the omni rim, and at any distance from the eye. Still decided by THIS fragment's own
// position — no column, ground line, bounds or per-object step (see HANDOVER 7.6b: that is how this broke before).
// The WALL shadow edge stays hard on purpose: a fade past a wall's reach would draw pixels that lie behind the
// wall, i.e. see through it. The band itself is still shadowed: the shadow row is cast to range + F and covers
// the cone's angle plus a margin, so a band pixel is tested against the reach of ITS OWN ray.
#ifndef LAUBRARY_VISION_MASK_INCLUDED
#define LAUBRARY_VISION_MASK_INCLUDED

#define VISION_MASK_MAX_CONES 8

float  _VisionMaskCount;    // live cones, 0..8
float  _VisionMaskPlane;    // 0 = XY (2D games), 1 = XZ (top-down 3D)
float  _VisionMaskBypass;   // 1 = everything visible (debug X-ray)
float4 _VisionMaskA[VISION_MASK_MAX_CONES];   // xy = eye (plane coords), zw = unit facing (plane coords)
float4 _VisionMaskB[VISION_MASK_MAX_CONES];   // x = cos(half angle), y = range, z = omni radius, w = half angle (rad)
float4 _VisionMaskC[VISION_MASK_MAX_CONES];   // x = occluded (0/1), y = texels in this cone's shadow row,
                                              // z = half angle the shadow row covers (rad), w = edge fade (world units)
TEXTURE2D(_VisionMaskShadow);                 // RFloat, width = max rays, row i = cone i: reach along each ray

float2 VisionMaskPlaneCoords(float3 world)
{
    return _VisionMaskPlane < 0.5 ? world.xy : world.xz;
}

// 1 on or inside the lit region (dist <= 0), 0 at or beyond `fade` past it, a smoothstep between. fade 0 = hard.
float VisionMaskFade(float dist, float fade)
{
    if (dist <= 0.0) return 1.0;
    if (fade <= 0.0 || dist >= fade) return 0.0;
    return 1.0 - smoothstep(0.0, fade, dist);
}

// One cone's visibility (0..1) for one plane point.
float VisionMaskConeVisibility(int i, float2 p)
{
    float4 a = _VisionMaskA[i];
    float4 b = _VisionMaskB[i];
    float4 c = _VisionMaskC[i];
    float fade = c.w;
    float2 d = p - a.xy;
    float r2 = dot(d, d);

    // Omni disc: seen in every direction and through occluders (a body glow, like the fog asset's
    // UnobscuredRadius and the City's omni disc). Its fade band is unoccluded too, by the same rule.
    if (r2 <= b.z * b.z) return 1.0;
    float vis = b.z > 0.0 ? VisionMaskFade(sqrt(r2) - b.z, fade) : 0.0;

    // Every point of the sector is within `range` of the eye, so beyond range + fade nothing can be in the band.
    float outer = b.y + fade;
    if (r2 > outer * outer || r2 <= 1e-10) return vis;

    float r = sqrt(r2);
    float2 dir = d / r;
    float2 f = a.zw;
    float dist;
    // Inside the cone's angle: only the range can be crossed. A full 360° cone (half angle π) has no angle limit
    // at all: comparing against cos(π) = -1 would let rounding (dot = -1.0000001) reject the pixels lying exactly
    // behind its facing — a one-pixel straight cut through a round light. So the test is skipped outright for it.
    if (b.w >= 3.14159 || dot(dir, f) >= b.x)
    {
        dist = r2 <= b.y * b.y ? 0.0 : r - b.y;   // the old squared test decides "inside", so fade 0 is bit-identical
    }
    else
    {
        if (fade <= 0.0) return vis;               // hard edge: outside the angle is outside
        // Outside the angle: the nearest lit point lies on the edge ray on this pixel's side (its segment
        // eye..eye + e*range; the arc's nearest point is that segment's far end, so the segment covers it).
        float side = (f.x * d.y - f.y * d.x) >= 0.0 ? 1.0 : -1.0;   // CCW positive
        float sn = sin(b.w) * side, cs = cos(b.w);
        float2 e = float2(f.x * cs - f.y * sn, f.x * sn + f.y * cs);
        float t = clamp(dot(d, e), 0.0, b.y);
        dist = length(d - e * t);
    }
    if (dist > 0.0 && dist >= fade) return vis;

    if (c.x >= 0.5)
    {
        // Occluded: the ray this pixel lies on stops at the first occluder; look its reach up. HARD — see above.
        float ang = atan2(f.x * dir.y - f.y * dir.x, dot(f, dir));   // signed, CCW positive
        float u = (ang / max(c.z, 1e-6) + 1.0) * 0.5;                // 0..1 across the covered angle
        int rays = (int)c.y;
        int col = clamp((int)floor(u * rays), 0, rays - 1);
        float reach = LOAD_TEXTURE2D(_VisionMaskShadow, int2(col, i)).r;
        if (r2 > reach * reach) return vis;
    }
    return max(vis, VisionMaskFade(dist, fade));
}

// How visible the world point is: the brightest verdict of ANY live cone (a union), 0..1.
float VisionMaskVisibility(float3 world)
{
    if (_VisionMaskBypass > 0.5) return 1.0;
    float2 p = VisionMaskPlaneCoords(world);
    int n = (int)_VisionMaskCount;
    float best = 0.0;
    [loop] for (int i = 0; i < VISION_MASK_MAX_CONES; i++)
    {
        if (i >= n) break;
        best = max(best, VisionMaskConeVisibility(i, p));
        if (best >= 1.0) break;
    }
    return best;
}

#endif
