// Shared per-pixel vision test for everything drawn on a Lattice: roads,
// sprites, props. Driven by global shader parameters set from
// Laubrary.LatticePixels.LatticeVision, so every material sees the same
// cone/disc (and shadow map in occluded mode) as the road view.
#ifndef LATTICE_VISION_INCLUDED
#define LATTICE_VISION_INCLUDED

float4 _LatticeOrigin;        // world origin of the lattice
float  _LatticePlane;         // 0 = XZ, 1 = XY
float4 _LatticeVisionPos;     // viewer position, lattice-plane coords
float4 _LatticeVisionDir;     // facing, lattice-plane coords
float  _LatticeVisionCos;     // cos of the cone half angle
float  _LatticeVisionRange;   // cone range, world units
float  _LatticeVisionOmni;    // omni disc radius, world units
float  _LatticeVisionOn;      // 0 = nothing visible
float  _LatticeVisionMode;    // 0 = cone, 1 = occluded (shadow map)
float  _LatticeVisionHalfAngle; // radians
TEXTURE2D(_LatticeShadowMap); SAMPLER(sampler_LatticeShadowMap);

// World position -> lattice-plane (u, v).
float2 LatticePlaneCoords(float3 world)
{
    float3 r = world - _LatticeOrigin.xyz;
    return _LatticePlane < 0.5 ? r.xz : r.xy;
}

// Is a lattice-plane point inside the viewer's vision?
bool LatticeVisible(float2 p)
{
    if (_LatticeVisionOn < 0.5) return false;
    float2 d = p - _LatticeVisionPos.xy;
    float r2 = dot(d, d);
    if (r2 <= _LatticeVisionOmni * _LatticeVisionOmni) return true;
    if (r2 > _LatticeVisionRange * _LatticeVisionRange || r2 <= 1e-8) return false;

    float2 dir = d * rsqrt(r2);
    float2 f = normalize(_LatticeVisionDir.xy);
    if (_LatticeVisionMode < 0.5)
        return dot(dir, f) >= _LatticeVisionCos;

    // Occluded: look the pixel's angle up in the shadow map.
    float ang = atan2(f.x * dir.y - f.y * dir.x, dot(f, dir));
    float u = (ang / _LatticeVisionHalfAngle + 1.0) * 0.5;
    if (u < 0.0 || u > 1.0) return false;
    float reach = SAMPLE_TEXTURE2D(_LatticeShadowMap, sampler_LatticeShadowMap, float2(u, 0.5)).r;
    return sqrt(r2) <= reach;
}

#endif
