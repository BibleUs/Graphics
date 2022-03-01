
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"

#define CLAMP_MAX       65472.0 // HALF_MAX minus one (2 - 2^-9) * 2^15

// Legacy defines, DON'T USE IN NEW PASSES THAT USE TEMPORAL AA
#define RADIUS              0.75

#if !defined(CTYPE)
    #define CTYPE float3
    #define CTYPE_SWIZZLE xyz
#endif

#if UNITY_REVERSED_Z
    #define COMPARE_DEPTH(a, b) step(b, a)
#else
    #define COMPARE_DEPTH(a, b) step(a, b)
#endif

float2 ClampAndScaleForBilinearWithCustomScale(float2 uv, float2 scale)
{
    float2 maxCoord = 1.0f - _ScreenSize.zw;
    return min(uv, maxCoord) * scale;
}

float3 Fetch(TEXTURE2D_X(tex), float2 coords, float2 offset, float2 scale)
{
    float2 uv = (coords + offset * _ScreenSize.zw);
    uv = ClampAndScaleForBilinearWithCustomScale(uv, scale);
    return SAMPLE_TEXTURE2D_X_LOD(tex, s_linear_clamp_sampler, uv, 0).xyz;
}

float4 Fetch4(TEXTURE2D_X(tex), float2 coords, float2 offset, float2 scale)
{
    float2 uv = (coords + offset * _ScreenSize.zw);
    uv = ClampAndScaleForBilinearWithCustomScale(uv, scale);
    return SAMPLE_TEXTURE2D_X_LOD(tex, s_linear_clamp_sampler, uv, 0);
}

float4 Fetch4Array(Texture2DArray tex, uint slot, float2 coords, float2 offset, float2 scale)
{
    float2 uv = (coords + offset * _ScreenSize.zw);
    uv = ClampAndScaleForBilinearWithCustomScale(uv, scale);
    return SAMPLE_TEXTURE2D_ARRAY_LOD(tex, s_linear_clamp_sampler, uv, slot, 0);
}

// ---------------------------------------------------
// Options
// ---------------------------------------------------

#define SHARPEN_ALPHA 0 // switch to 1 if you want to enable TAA sharpenning on alpha channel

// History sampling options
#define BILINEAR 0
#define BICUBIC_5TAP 1

/// Neighbourhood sampling options
#define PLUS 0    // Faster! Can allow for read across twice (paying cost of 2 samples only)
#define CROSS 1   // Can only do one fast read diagonal
#define SMALL_NEIGHBOURHOOD_SHAPE PLUS

// Neighbourhood AABB options
#define MINMAX 0
#define VARIANCE 1

// Central value filtering options
#define NO_FILTERING 0
#define BOX_FILTER 1
#define SHARP_FILTER 2
#define UPSCALE 3

// Clip option
#define DIRECT_CLIP 0
#define BLEND_WITH_CLIP 1
#define SIMPLE_CLAMP 2

// Upsample pixel confidence factor (used for tuning the blend factor when upsampling)
// See A Survey of Temporal Antialiasing Techniques [Yang et al 2020], section 5.1
#define GAUSSIAN_WEIGHT 0
#define BOX_REJECT 1
#define CONFIDENCE_FACTOR BOX_REJECT

#if CENTRAL_FILTERING == UPSCALE
#define UPSAMPLE
#endif

// Set defines in case not set outside the include
#ifndef YCOCG
    #define YCOCG 1
#endif

#ifndef HISTORY_SAMPLING_METHOD
    #define HISTORY_SAMPLING_METHOD BILINEAR
#endif

#ifndef NEIGHBOUROOD_CORNER_METHOD
    #define NEIGHBOUROOD_CORNER_METHOD VARIANCE
#endif

#ifndef WIDE_NEIGHBOURHOOD
    #define WIDE_NEIGHBOURHOOD 0
#endif

#ifndef CENTRAL_FILTERING
    #define CENTRAL_FILTERING NO_FILTERING
#endif

#ifndef CENTRAL_FILTERING
    #define CENTRAL_FILTERING DIRECT_CLIP
#endif

#ifndef ANTI_FLICKER
    #define ANTI_FLICKER 1
#endif

#ifndef VELOCITY_REJECTION
    #define VELOCITY_REJECTION 0
#endif

#ifndef PERCEPTUAL_SPACE
    #define PERCEPTUAL_SPACE 1
#endif


// ---------------------------------------------------
// Utilities functions
// ---------------------------------------------------

float3 Min3Float3(float3 a, float3 b, float3 c)
{
    return float3(Min3(a.x, b.x, c.x),
        Min3(a.y, b.y, c.y),
        Min3(a.z, b.z, c.z));
}

float3 MinFloat3(float3 a, float3 b)
{
    return float3(min(a.x, b.x), min(a.y, b.y), min(a.z, b.z));
}

float4 MinFloat4(float4 a, float4 b)
{
    return float4(min(a.x, b.x), min(a.y, b.y), min(a.z, b.z), min(a.w, b.w));
}

float3 MaxFloat3(float3 a, float3 b)
{
    return float3(max(a.x, b.x), max(a.y, b.y), max(a.z, b.z));
}

float4 MaxFloat4(float4 a, float4 b)
{
    return float4(max(a.x, b.x), max(a.y, b.y), max(a.z, b.z), max(a.w, b.w));
}

float3 Max3Float3(float3 a, float3 b, float3 c)
{
    return float3(Max3(a.x, b.x, c.x),
        Max3(a.y, b.y, c.y),
        Max3(a.z, b.z, c.z));
}

float4 Min3Float4(float4 a, float4 b, float4 c)
{
    return float4(Min3(a.x, b.x, c.x),
        Min3(a.y, b.y, c.y),
        Min3(a.z, b.z, c.z),
        Min3(a.w, b.w, c.w));
}

float4 Max3Float4(float4 a, float4 b, float4 c)
{
    return float4(Max3(a.x, b.x, c.x),
        Max3(a.y, b.y, c.y),
        Max3(a.z, b.z, c.z),
        Max3(a.w, b.w, c.w));
}

CTYPE Max3Color(CTYPE a, CTYPE b, CTYPE c)
{
#ifdef ENABLE_ALPHA
    return Max3Float4(a, b, c);
#else
    return Max3Float3(a, b, c);
#endif
}

CTYPE MaxColor(CTYPE a, CTYPE b)
{
#ifdef ENABLE_ALPHA
    return MaxFloat4(a, b);
#else
    return MaxFloat3(a, b);
#endif
}

CTYPE MinColor(CTYPE a, CTYPE b)
{
#ifdef ENABLE_ALPHA
    return MinFloat4(a, b);
#else
    return MinFloat3(a, b);
#endif
}

CTYPE Min3Color(CTYPE a, CTYPE b, CTYPE c)
{
#ifdef ENABLE_ALPHA
    return Min3Float4(a, b, c);
#else
    return Min3Float3(a, b, c);
#endif
}

// Define Quad communication functions
CTYPE QuadReadColorAcrossX(CTYPE c, int2 positionSS)
{
#ifdef ENABLE_ALPHA
    return QuadReadFloat4AcrossX(c, positionSS);
#else
    return QuadReadFloat3AcrossX(c, positionSS);
#endif
}

CTYPE QuadReadColorAcrossY(CTYPE c, int2 positionSS)
{
#ifdef ENABLE_ALPHA
    return QuadReadFloat4AcrossY(c, positionSS);
#else
    return QuadReadFloat3AcrossY(c, positionSS);
#endif
}

CTYPE QuadReadColorAcrossDiagonal(CTYPE c, int2 positionSS)
{
#ifdef ENABLE_ALPHA
    return QuadReadFloat4AcrossDiagonal(c, positionSS);
#else
    return QuadReadFloat3AcrossDiagonal(c, positionSS);
#endif
}

// ---------------------------------------------------
// Color related utilities.
// ---------------------------------------------------


float GetLuma(CTYPE color)
{
#if YCOCG
    // We work in YCoCg hence the luminance is in the first channel.
    return color.x;
#else
    return Luminance(color.xyz);
#endif
}

CTYPE ReinhardToneMap(CTYPE c)
{
    return c * rcp(GetLuma(c) + 1.0);
}

float PerceptualWeight(CTYPE c)
{
#if PERCEPTUAL_SPACE
    return rcp(GetLuma(c) + 1.0);
#else
    return 1;
#endif
}

CTYPE InverseReinhardToneMap(CTYPE c)
{
    return c * rcp(1.0 - GetLuma(c));
}

float PerceptualInvWeight(CTYPE c)
{
#if PERCEPTUAL_SPACE
    return rcp(1.0 - GetLuma(c));
#else
    return 1;
#endif
}

CTYPE ConvertToWorkingSpace(CTYPE rgb)
{
#if YCOCG
    float3 ycocg = RGBToYCoCg(rgb.xyz);

    #if ENABLE_ALPHA
        return float4(ycocg, rgb.a);
    #else
        return ycocg;
    #endif

#else
    return rgb;
#endif

}
float3 ConvertToOutputSpace(float3 color)
{
#if YCOCG
    return YCoCgToRGB(color);
#else
    return color;
#endif
}

// ---------------------------------------------------
// Velocity related functions.
// ---------------------------------------------------

// Front most neighbourhood velocity ([Karis 2014])
float2 GetClosestFragmentOffset(TEXTURE2D_X(DepthTexture), int2 positionSS, int searchWidth)
{
    float center = LOAD_TEXTURE2D_X_LOD(DepthTexture, positionSS, 0).r;

    float s0 = LOAD_TEXTURE2D_X_LOD(DepthTexture, positionSS + int2(-searchWidth, -searchWidth), 0).r;
    float s1 = LOAD_TEXTURE2D_X_LOD(DepthTexture, positionSS + int2( searchWidth, -searchWidth), 0).r;
    float s2 = LOAD_TEXTURE2D_X_LOD(DepthTexture, positionSS + int2(-searchWidth,  searchWidth), 0).r;
    float s3 = LOAD_TEXTURE2D_X_LOD(DepthTexture, positionSS + int2( searchWidth,  searchWidth), 0).r;

    float3 closest = float3(0.0, 0.0, center);
    closest = COMPARE_DEPTH(s0, closest.z) ? float3(int2(-searchWidth, -searchWidth), s0) : closest;
    closest = COMPARE_DEPTH(s1, closest.z) ? float3(int2( searchWidth, -searchWidth), s1) : closest;
    closest = COMPARE_DEPTH(s2, closest.z) ? float3(int2(-searchWidth,  searchWidth), s2) : closest;
    closest = COMPARE_DEPTH(s3, closest.z) ? float3(int2( searchWidth,  searchWidth), s3) : closest;

    return closest.xy;
}

// Used since some compute might want to call this and we cannot use Quad reads in that case.
float2 GetClosestFragmentCompute(float2 positionSS)
{
    float center = LoadCameraDepth(positionSS);
    float nw = LoadCameraDepth(positionSS + int2(-1, -1));
    float ne = LoadCameraDepth(positionSS + int2(1, -1));
    float sw = LoadCameraDepth(positionSS + int2(-1, 1));
    float se = LoadCameraDepth(positionSS + int2(1, 1));

    float4 neighborhood = float4(nw, ne, sw, se);

    float3 closest = float3(0.0, 0.0, center);
    closest = lerp(closest, float3(-1, -1, neighborhood.x), COMPARE_DEPTH(neighborhood.x, closest.z));
    closest = lerp(closest, float3(1, -1, neighborhood.y), COMPARE_DEPTH(neighborhood.y, closest.z));
    closest = lerp(closest, float3(-1, 1, neighborhood.z), COMPARE_DEPTH(neighborhood.z, closest.z));
    closest = lerp(closest, float3(1, 1, neighborhood.w), COMPARE_DEPTH(neighborhood.w, closest.z));

    return positionSS + closest.xy;
}


float ModifyBlendWithMotionVectorRejection(TEXTURE2D_X(VelocityMagnitudeTexture), float mvLen, float2 prevUV, float blendFactor, float speedRejectionFactor, float2 rtHandleScale)
{
    // TODO: This needs some refinement, it can lead to some annoying flickering coming back on strong camera movement.
#if VELOCITY_REJECTION

    float prevMVLen = Fetch(VelocityMagnitudeTexture, prevUV, 0, rtHandleScale).x;
    float diff = abs(mvLen - prevMVLen);

    // We don't start rejecting until we have the equivalent of around 40 texels in 1080p
    diff -= 0.015935382;
    float val = saturate(diff * speedRejectionFactor);
    return lerp(blendFactor, 0.97f, val*val);

#else
    return blendFactor;
#endif
}

// ---------------------------------------------------
// History sampling
// ---------------------------------------------------

CTYPE HistoryBilinear(TEXTURE2D_X(HistoryTexture), float2 UV, float2 rtHandleScale)
{
    CTYPE color = Fetch4(HistoryTexture, UV, 0.0, rtHandleScale).CTYPE_SWIZZLE;
    return color;
}

// From Filmic SMAA presentation[Jimenez 2016]
// A bit more verbose that it needs to be, but makes it a bit better at latency hiding
CTYPE HistoryBicubic5Tap(TEXTURE2D_X(HistoryTexture), float2 UV, float sharpening, float4 historyBufferInfo, float2 rtHandleScale)
{
    float2 samplePos = UV * historyBufferInfo.xy;
    float2 tc1 = floor(samplePos - 0.5) + 0.5;
    float2 f = samplePos - tc1;
    float2 f2 = f * f;
    float2 f3 = f * f2;

    const float c = sharpening;

    float2 w0 = -c         * f3 +  2.0 * c         * f2 - c * f;
    float2 w1 =  (2.0 - c) * f3 - (3.0 - c)        * f2          + 1.0;
    float2 w2 = -(2.0 - c) * f3 + (3.0 - 2.0 * c)  * f2 + c * f;
    float2 w3 = c          * f3 - c                * f2;

    float2 w12 = w1 + w2;
    float2 tc0 = historyBufferInfo.zw   * (tc1 - 1.0);
    float2 tc3 = historyBufferInfo.zw   * (tc1 + 2.0);
    float2 tc12 = historyBufferInfo.zw  * (tc1 + w2 / w12);

    CTYPE s0 = Fetch4(HistoryTexture, float2(tc12.x, tc0.y), 0.0, rtHandleScale).CTYPE_SWIZZLE;
    CTYPE s1 = Fetch4(HistoryTexture, float2(tc0.x, tc12.y), 0.0, rtHandleScale).CTYPE_SWIZZLE;
    CTYPE s2 = Fetch4(HistoryTexture, float2(tc12.x, tc12.y), 0.0, rtHandleScale).CTYPE_SWIZZLE;
    CTYPE s3 = Fetch4(HistoryTexture, float2(tc3.x, tc0.y), 0.0, rtHandleScale).CTYPE_SWIZZLE;
    CTYPE s4 = Fetch4(HistoryTexture, float2(tc12.x, tc3.y), 0.0, rtHandleScale).CTYPE_SWIZZLE;

    float cw0 = (w12.x * w0.y);
    float cw1 = (w0.x * w12.y);
    float cw2 = (w12.x * w12.y);
    float cw3 = (w3.x * w12.y);
    float cw4 = (w12.x *  w3.y);

#ifdef ANTI_RINGING
    CTYPE min = Min3Color(s0, s1, s2);
    min = Min3Color(min, s3, s4);

    CTYPE max = Max3Color(s0, s1, s2);
    max = Max3Color(max, s3, s4);
#endif

    s0 *= cw0;
    s1 *= cw1;
    s2 *= cw2;
    s3 *= cw3;
    s4 *= cw4;

    CTYPE historyFiltered = s0 + s1 + s2 + s3 + s4;
    float weightSum = cw0 + cw1 + cw2 + cw3 + cw4;

    CTYPE filteredVal = historyFiltered.CTYPE_SWIZZLE * rcp(weightSum);

#if ANTI_RINGING
    // This sortof neighbourhood clamping seems to work to avoid the appearance of overly dark outlines in case
    // sharpening of history is too strong.
    return clamp(filteredVal, min, max);
#endif

    return filteredVal;
}


CTYPE GetFilteredHistory(TEXTURE2D_X(HistoryTexture), float2 UV, float sharpening, float4 historyBufferInfo, float2 rtHandleScale)
{
    CTYPE history = 0;

#if (HISTORY_SAMPLING_METHOD == BILINEAR || defined(FORCE_BILINEAR_HISTORY))
    history = HistoryBilinear(HistoryTexture, UV, rtHandleScale);
#elif HISTORY_SAMPLING_METHOD == BICUBIC_5TAP
    history = HistoryBicubic5Tap(HistoryTexture, UV, sharpening, historyBufferInfo, rtHandleScale);
#endif

    history = clamp(history, 0, CLAMP_MAX);

    return ConvertToWorkingSpace(history);
}


// ---------------------------------------------------
// Neighbourhood related.
// ---------------------------------------------------
#define SMALL_NEIGHBOURHOOD_SIZE 4
#define NEIGHBOUR_COUNT ((WIDE_NEIGHBOURHOOD == 0) ? SMALL_NEIGHBOURHOOD_SIZE : 8)

static const float2 sampleOffsets[9] =
{
    float2( 1,  0),
    float2(-1,  0),
    float2( 0,  1),
    float2( 0, -1),
    float2( 1,  1),
    float2(-1,  1),
    float2( 1, -1),
    float2(-1, -1),
    float2( 0,  0), // Kept last to be able to offset into it no matter the shape.
};

struct NeighbourhoodInfo
{
    CTYPE filteredCentral;
    CTYPE minNeighbour;
    CTYPE maxNeighbour;
    CTYPE boxFiltered;

#if NEIGHBOUROOD_CORNER_METHOD == VARIANCE
    CTYPE moment1;
    CTYPE moment2;
#endif
};

// TODO: Can do better.
void InitNeighbourhoodInfo(inout NeighbourhoodInfo info)
{
    info = (NeighbourhoodInfo)0;
    info.minNeighbour = 10e10f;
    info.maxNeighbour = -10e10f;
}

void UpdateNeighbourhoodCornersInfo(CTYPE currSample, inout NeighbourhoodInfo samples)
{
#if NEIGHBOUROOD_CORNER_METHOD == VARIANCE
    currSample.xyz = clamp(currSample.xyz, 0, CLAMP_MAX);

    samples.moment1 += currSample * rcp(NEIGHBOUR_COUNT + 1);
    samples.moment2 += (currSample * currSample) * rcp(NEIGHBOUR_COUNT + 1);

#elif NEIGHBOUROOD_CORNER_METHOD == MINMAX

    samples.minNeighbour = MinColor(currSample, samples.minNeighbour);
    samples.maxNeighbour = MaxColor(currSample, samples.maxNeighbour);

#endif

    samples.boxFiltered += currSample * rcp(NEIGHBOUR_COUNT + 1);
}

void UpdateCentralColor(CTYPE currSample, float2 distToSampleCenter, float renderScale, float filterSharpness, inout CTYPE filteredColor, inout float totalWeight, out float currWeight)
{
    currWeight = 1;
#if CENTRAL_FILTERING == NO_FILTERING
    totalWeight = 1;
    return;
#elif CENTRAL_FILTERING == BOX_FILTER
    // Nothing to be done, currWeight default is box filter.
#elif CENTRAL_FILTERING == SHARP_FILTER
    const float sharpness = filterSharpness;
    currWeight = (exp2(-sharpness * dot(distToSampleCenter, distToSampleCenter) * renderScale)); // renderScale is != 1 only for TAAU
#endif

    filteredColor += currWeight*currSample;
    totalWeight += currWeight;
}

void ResolveNeighbourhoodCorners(inout NeighbourhoodInfo samples, float historyLuma, float colorLuma, float2 antiFlickerParams, float motionVecLenInPixels, float downsampleFactor)
{
#if NEIGHBOUROOD_CORNER_METHOD == VARIANCE
    CTYPE stdDev = sqrt(abs(samples.moment2 - samples.moment1 * samples.moment1));

    float stDevMultiplier = 1.5;
    // The reasoning behind the anti flicker is that if we have high spatial contrast (high standard deviation)
    // and high temporal contrast, we let the history to be closer to be unclipped. To achieve, the min/max bounds
    // are extended artificially more.
#if ANTI_FLICKER
    stDevMultiplier = 1.5;
    float temporalContrast = saturate(abs(colorLuma - historyLuma) / Max3(0.2, colorLuma, historyLuma));
#if ANTI_FLICKER_MV_DEPENDENT
    const float maxFactorScale = 2.25f; // when stationary
    const float minFactorScale = 0.8f; // when moving more than slightly
    float localizedAntiFlicker = lerp(antiFlickerParams.x * minFactorScale, antiFlickerParams.x * maxFactorScale, saturate(1.0f - 2.0f * (motionVecLenInPixels)));
#else
    float localizedAntiFlicker = antiFlickerParams.x;
#endif
    stDevMultiplier += lerp(0.0, localizedAntiFlicker, smoothstep(0.05, antiFlickerParams.y, temporalContrast));
#endif

#ifdef TAA_UPSCALE
    // We shrink the bounding box when upscaling as ghosting is more likely.
    // Ideally the shrinking should happen also (or just) when sampling the neighbours
    // This shrinking should also be investigated a bit further with more content. (TODO).
    stDevMultiplier = lerp(stDevMultiplier, 0.9f, saturate(downsampleFactor));
#endif

    samples.minNeighbour = samples.moment1 - stdDev * stDevMultiplier;
    samples.maxNeighbour = samples.moment1 + stdDev * stDevMultiplier;
#endif
}

// For portability reason we assume positionSS is int2 (so can be used with dispatchThreadId)
CTYPE GatherNeighbourhoodAndFilterColor(TEXTURE2D_X(InputTexture), int2 positionSS, float2 jitterInPixels, float renderScale, float filterSharpness, inout NeighbourhoodInfo samples)
{
    // Make sure we zero and init with starting values all of our infos.
    InitNeighbourhoodInfo(samples);

    const int sampleCount = NEIGHBOUR_COUNT + 1;

    float2 dstPosSS = positionSS + 0.5f;
    int2 posInInput = int2((positionSS + 0.5f) * renderScale);
    // sample loc but in the upsampled space.
    float2 centralPosInOutputSpace = (posInInput + 0.5f + jitterInPixels) / renderScale;

    float totalWeight = 0;
    CTYPE filteredColor = 0;

    CTYPE centralUnfiltered = 0;
    // TODO_FCC: FIX NO FILTERING.
    float2 samplePosSS = 0;
    float sampleWeight = 0;
    CTYPE sampleVal = 0;
    for (int i = 0; i < sampleCount-1; ++i)
    {
        int offsetIndex = i;
#if (WIDE_NEIGHBOURHOOD == 0) && (SMALL_NEIGHBOURHOOD_SHAPE == CROSS)
        offsetIndex += 4;
#endif
        int2 offset = sampleOffsets[offsetIndex];

        sampleVal = InputTexture[COORD_TEXTURE2D_X(posInInput + offset)];
        sampleVal = ConvertToWorkingSpace(sampleVal);
        sampleVal *= PerceptualWeight(sampleVal);

        samplePosSS = centralPosInOutputSpace + offset * rcp(renderScale);

#if CENTRAL_FILTERING != NO_FILTERING
        UpdateCentralColor(sampleVal, (samplePosSS - dstPosSS), renderScale, filterSharpness, filteredColor, totalWeight, sampleWeight);
        centralUnfiltered = sampleVal;
#endif
        UpdateNeighbourhoodCornersInfo(sampleVal, samples);
    }

    // We now process the central pixel slightly differently.
    // If we are not filtering at all we need to use linear sampler.

#if CENTRAL_FILTERING == NO_FILTERING
    float2 uv = (posInInput + 0.5) * (renderScale * _ScreenSize.zw);
    sampleVal = SAMPLE_TEXTURE2D_X_LOD(InputTexture, s_linear_clamp_sampler, uv, 0).xyz;
#else
    sampleVal = InputTexture[COORD_TEXTURE2D_X(posInInput)];
#endif
    sampleVal = ConvertToWorkingSpace(sampleVal);
    sampleVal *= PerceptualWeight(sampleVal);

#if CENTRAL_FILTERING != NO_FILTERING
    UpdateCentralColor(sampleVal, (centralPosInOutputSpace - dstPosSS), renderScale, filterSharpness, filteredColor, totalWeight, sampleWeight);
#endif
    UpdateNeighbourhoodCornersInfo(sampleVal, samples);

    if (totalWeight < 1e-5f
#if CENTRAL_FILTERING == NO_FILTERING
        || true
#endif
        )
        samples.filteredCentral = sampleVal;
    else
        samples.filteredCentral = filteredColor / totalWeight;


    return samples.filteredCentral;
}

// ---------------------------------------------------
// Blend factor calculation
// ---------------------------------------------------

float HistoryContrast(float historyLuma, float minNeighbourLuma, float maxNeighbourLuma, float baseBlendFactor)
{
    float lumaContrast = max(maxNeighbourLuma - minNeighbourLuma, 0) / historyLuma;
    float blendFactor = baseBlendFactor;
    return saturate(blendFactor * rcp(1.0 + lumaContrast));
}

float DistanceToClamp(float historyLuma, float minNeighbourLuma, float maxNeighbourLuma)
{
    float distToClamp = min(abs(minNeighbourLuma - historyLuma), abs(maxNeighbourLuma - historyLuma));
    return saturate((0.125 * distToClamp) / (distToClamp + maxNeighbourLuma - minNeighbourLuma));
}

float GetBlendFactor(float colorLuma, float historyLuma, float minNeighbourLuma, float maxNeighbourLuma, float baseBlendFactor)
{
    // TODO: Investigate factoring in the speed in this computation.

    return HistoryContrast(historyLuma, minNeighbourLuma, maxNeighbourLuma, baseBlendFactor);
}

// ---------------------------------------------------
// Clip History
// ---------------------------------------------------

// From Playdead's TAA
CTYPE DirectClipToAABB(CTYPE history, CTYPE minimum, CTYPE maximum)
{
    // note: only clips towards aabb center (but fast!)
    CTYPE center  = 0.5 * (maximum + minimum);
    CTYPE extents = 0.5 * (maximum - minimum);

    // This is actually `distance`, however the keyword is reserved
    CTYPE offset = history - center;
    float3 v_unit = offset.xyz / extents.xyz;
    float3 absUnit = abs(v_unit);
    float maxUnit = Max3(absUnit.x, absUnit.y, absUnit.z);

    if (maxUnit > 1.0)
        return center + (offset / maxUnit);
    else
        return history;
}

// Here the ray referenced goes from history to (filtered) center color
float DistToAABB(CTYPE color, CTYPE history, CTYPE minimum, CTYPE maximum)
{
    CTYPE center = 0.5 * (maximum + minimum);
    CTYPE extents = 0.5 * (maximum - minimum);

    CTYPE rayDir = color - history;
    CTYPE rayPos = history - center;

    CTYPE invDir = rcp(rayDir);
    CTYPE t0 = (extents - rayPos)  * invDir;
    CTYPE t1 = -(extents + rayPos) * invDir;

    float AABBIntersection = max(max(min(t0.x, t1.x), min(t0.y, t1.y)), min(t0.z, t1.z));
    return saturate(AABBIntersection);
}

CTYPE GetClippedHistory(CTYPE filteredColor, CTYPE history, CTYPE minimum, CTYPE maximum)
{
#if HISTORY_CLIP == DIRECT_CLIP
    return DirectClipToAABB(history, minimum, maximum);
#elif HISTORY_CLIP == BLEND_WITH_CLIP
    float historyBlend = DistToAABB(filteredColor, history, minimum, maximum);
    return lerp(history, filteredColor, historyBlend);
#elif HISTORY_CLIP == SIMPLE_CLAMP
    return clamp(history, minimum, maximum);
#endif
}

// ---------------------------------------------------
// Sharpening
// ---------------------------------------------------

// TODO: This is not great and sub optimal since it really needs to be in linear and the data is already in perceptive space
CTYPE SharpenColor(NeighbourhoodInfo samples, CTYPE color, float sharpenStrength)
{
    CTYPE linearC = color * PerceptualInvWeight(color);
    CTYPE linearAvg = samples.boxFiltered * PerceptualInvWeight(samples.boxFiltered);

#if YCOCG
    // Rotating back to RGB it leads to better behaviour when sharpening, a better approach needs definitively to be investigated in the future.

    linearC.xyz = ConvertToOutputSpace(linearC.xyz);
    linearAvg.xyz = ConvertToOutputSpace(linearAvg.xyz);
    linearC.xyz = linearC.xyz + max(0, (linearC.xyz - linearAvg.xyz)) * sharpenStrength * 3;
    linearC.xyz = clamp(linearC.xyz, 0, CLAMP_MAX);

    linearC = ConvertToWorkingSpace(linearC);
#else
    linearC = linearC + max(0,(linearC - linearAvg)) * sharpenStrength * 3;
    linearC = clamp(linearC, 0, CLAMP_MAX);
#endif
    CTYPE outputSharpened = linearC * PerceptualWeight(linearC);

#if (SHARPEN_ALPHA == 0 && defined(ENABLE_ALPHA))
    outputSharpened.a = color.a;
#endif

    return outputSharpened;
}

// ---------------------------------------------------
// Upscale confidence factor
// ---------------------------------------------------

// Binary accept or not
float BoxKernelConfidence(float2 inputToOutputVec, float confidenceThreshold)
{
    // Binary (TODO: Smooth it?)
    float confidenceScore = abs(inputToOutputVec.x) <= confidenceThreshold && abs(inputToOutputVec.y) <= confidenceThreshold;
    return confidenceScore;
}

float GaussianConfidence(float2 inputToOutputVec, float rcpStdDev2, float resScale)
{
    const float resolutionScale2 = resScale * resScale;

    return resolutionScale2 * exp2(-0.5f * dot(inputToOutputVec, inputToOutputVec) * resolutionScale2 * rcpStdDev2);
}

float GetUpsampleConfidence(float2 inputToOutputVec, float confidenceThreshold, float rcpStdDev2, float resScale)
{
#if CONFIDENCE_FACTOR == GAUSSIAN_WEIGHT
    return saturate(GaussianConfidence(inputToOutputVec, rcpStdDev2, resScale));
#elif CONFIDENCE_FACTOR == BOX_REJECT
    return BoxKernelConfidence(inputToOutputVec, confidenceThreshold);
#endif

    return 1;
}
