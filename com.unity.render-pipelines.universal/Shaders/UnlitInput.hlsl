#ifndef UNIVERSAL_UNLIT_INPUT_INCLUDED
#define UNIVERSAL_UNLIT_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
half4 _BaseColor;
half _Cutoff;
half _Glossiness;
half _Metallic;
CBUFFER_END

#ifdef UNITY_DOTS_INSTANCING_ENABLED
UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
    UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
    UNITY_DOTS_INSTANCED_PROP(float , _Cutoff)
    UNITY_DOTS_INSTANCED_PROP(float , _Glossiness)
    UNITY_DOTS_INSTANCED_PROP(float , _Metallic)
UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)

#define _BaseColor          UNITY_ACCESS_DOTS_INSTANCED_PROP_FROM_MACRO(half4 , Metadata__BaseColor)
#define _Cutoff             UNITY_ACCESS_DOTS_INSTANCED_PROP_FROM_MACRO(half  , Metadata__Cutoff)
#define _Glossiness         UNITY_ACCESS_DOTS_INSTANCED_PROP_FROM_MACRO(half  , Metadata__Glossiness)
#define _Metallic           UNITY_ACCESS_DOTS_INSTANCED_PROP_FROM_MACRO(half  , Metadata__Metallic)
#endif

#endif
