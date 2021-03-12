using System;
using System.Linq;
using UnityEngine.Experimental.Rendering;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using System.Runtime.InteropServices;
using System.Security.Permissions;

namespace UnityEngine.Rendering.Universal
{
    public class LightCookieManager : IDisposable
    {
        static class ShaderProperty
        {
            public static readonly int _MainLightTexture        = Shader.PropertyToID("_MainLightCookieTexture");
            public static readonly int _MainLightWorldToLight   = Shader.PropertyToID("_MainLightWorldToLight");
            public static readonly int _MainLightCookieUVScale  = Shader.PropertyToID("_MainLightCookieUVScale");
            public static readonly int _MainLightCookieFormat   = Shader.PropertyToID("_MainLightCookieFormat");

            public static readonly int _AdditionalLightsCookieAtlasTexture      = Shader.PropertyToID("_AdditionalLightsCookieAtlasTexture");
            public static readonly int _AdditionalLightsCookieAtlasFormat       = Shader.PropertyToID("_AdditionalLightsCookieAtlasFormat");

            public static readonly int _AdditionalLightsWorldToLightBuffer      = Shader.PropertyToID("_AdditionalLightsWorldToLightBuffer");    // TODO: really a light property
            public static readonly int _AdditionalLightsCookieAtlasUVRectBuffer = Shader.PropertyToID("_AdditionalLightsCookieAtlasUVRectBuffer");
            public static readonly int _AdditionalLightsLightTypeBuffer         = Shader.PropertyToID("_AdditionalLightsLightTypeBuffer");        // TODO: really a light property

            public static readonly int _AdditionalLightsWorldToLights           = Shader.PropertyToID("_AdditionalLightsWorldToLights");
            public static readonly int _AdditionalLightsCookieAtlasUVRects      = Shader.PropertyToID("_AdditionalLightsCookieAtlasUVRects");
            public static readonly int _AdditionalLightsLightTypes              = Shader.PropertyToID("_AdditionalLightsLightTypes");
        }
        public struct Settings
        {
            public struct AtlasSettings
            {
                public Vector2Int resolution;
                public GraphicsFormat format;

                public bool isPow2 => Mathf.IsPowerOfTwo(resolution.x) && Mathf.IsPowerOfTwo(resolution.x);
            }

            public AtlasSettings atlas;
            public int   maxAdditionalLights;        // UniversalRenderPipeline.maxVisibleAdditionalLights;
            public float cubeOctahedralSizeScale;    // Cube octahedral projection size scale.
            public bool  useStructuredBuffer;        // RenderingUtils.useStructuredBuffer

            public static Settings GetDefault()
            {
                Settings s;
                s.atlas.resolution    = new Vector2Int(1024, 1024);
                s.atlas.format        = GraphicsFormat.R8G8B8A8_SRGB; // TODO: optimize
                s.maxAdditionalLights = UniversalRenderPipeline.maxVisibleAdditionalLights;
                // (Scale * WH) / (6 * WH)
                // 1: 1/6 = 16%, 2: 4/6 = 66%, 4: 16/6 == 266% of cube pixels
                // 100% cube pixels == sqrt(6) ~= 2.45f;
                s.cubeOctahedralSizeScale = s.atlas.isPow2 ? 2.0f : 2.45f;
                s.useStructuredBuffer = RenderingUtils.useStructuredBuffer;
                return s;
            }
        }

        private struct LightCookieData : System.IComparable<LightCookieData>
        {
            public ushort visibleLightIndex; // Index into visible light (src) (after sorting)
            public ushort lightBufferIndex;  // Index into light shader data buffer (dst)
            public int priority;
            public float score;

            public int CompareTo(LightCookieData other)
            {
                if (priority > other.priority)
                    return -1;
                if (priority == other.priority)
                {
                    if (score > other.score)
                        return -1;
                    if (score == other.score)
                        return 0;
                }

                return 1;
            }
        }

        private class LightCookieShaderData : IDisposable
        {
            int m_Size = 0;
            bool m_useStructuredBuffer;

            Matrix4x4[] m_WorldToLightCpuData;
            Vector4[]   m_AtlasUVRectCpuData;
            float[]     m_LightTypeCpuData;

            // TODO: WorldToLight matrices should be general property of lights!!
            ComputeBuffer  m_WorldToLightBuffer;
            ComputeBuffer  m_AtlasUVRectBuffer;
            ComputeBuffer  m_LightTypeBuffer;

            public Matrix4x4[] worldToLights => m_WorldToLightCpuData;
            public Vector4[]   atlasUVRects  => m_AtlasUVRectCpuData;
            public float[]     lightTypes => m_LightTypeCpuData;

            public LightCookieShaderData(int size, bool useStructuredBuffer)
            {
                m_useStructuredBuffer = useStructuredBuffer;
                Resize(size);
            }

            public void Dispose()
            {
                if (m_useStructuredBuffer)
                {
                    m_WorldToLightBuffer?.Dispose();
                    m_AtlasUVRectBuffer?.Dispose();
                    m_LightTypeBuffer?.Dispose();
                }
            }

            public void Resize(int size)
            {
                if (size < m_Size)
                    return;

                if (m_Size > 0)
                    Dispose();

                if (m_useStructuredBuffer)
                {
                    m_WorldToLightBuffer = new ComputeBuffer(size, Marshal.SizeOf<Matrix4x4>());
                    m_AtlasUVRectBuffer  = new ComputeBuffer(size, Marshal.SizeOf<Vector4>());
                    m_LightTypeBuffer    = new ComputeBuffer(size, Marshal.SizeOf<float>());
                }
                else
                {
                    m_WorldToLightCpuData = new Matrix4x4[size];
                    m_AtlasUVRectCpuData  = new Vector4[size];
                    m_LightTypeCpuData    = new float[size];
                }

                m_Size = size;
            }

            public void Apply(CommandBuffer cmd)
            {
                if (m_useStructuredBuffer)
                {
                    m_WorldToLightBuffer.SetData(m_WorldToLightCpuData);
                    m_AtlasUVRectBuffer.SetData(m_AtlasUVRectCpuData);

                    cmd.SetGlobalBuffer(ShaderProperty._AdditionalLightsWorldToLightBuffer, m_WorldToLightBuffer);
                    cmd.SetGlobalBuffer(ShaderProperty._AdditionalLightsCookieAtlasUVRectBuffer, m_AtlasUVRectBuffer);
                    cmd.SetGlobalBuffer(ShaderProperty._AdditionalLightsLightTypeBuffer, m_LightTypeBuffer);
                }
                else
                {
                    cmd.SetGlobalMatrixArray(ShaderProperty._AdditionalLightsWorldToLights, m_WorldToLightCpuData);
                    cmd.SetGlobalVectorArray(ShaderProperty._AdditionalLightsCookieAtlasUVRects, m_AtlasUVRectCpuData);
                    cmd.SetGlobalFloatArray(ShaderProperty._AdditionalLightsLightTypes, m_LightTypeCpuData);
                }
            }
        }

        Texture2DAtlas        m_AdditionalLightsCookieAtlas;
        LightCookieShaderData m_AdditionalLightsCookieShaderData;
        Settings              m_Settings;

        public LightCookieManager(in Settings settings)
        {
            m_Settings = settings;
        }

        void InitAdditionalLights(int size)
        {
            if (m_Settings.atlas.isPow2)
            {
                m_AdditionalLightsCookieAtlas = new PowerOfTwoTextureAtlas(
                    m_Settings.atlas.resolution.x,
                    1,    // TODO: what's the correct value here, how many mips before bleed?
                    m_Settings.atlas.format,
                    FilterMode.Bilinear,    // TODO: option?
                    "Universal Light Cookie Atlas",
                    true);
            }
            else
            {
                m_AdditionalLightsCookieAtlas = new Texture2DAtlas(
                    m_Settings.atlas.resolution.x,
                    m_Settings.atlas.resolution.y,
                    m_Settings.atlas.format,
                    FilterMode.Bilinear,    // TODO: option?
                    m_Settings.atlas.isPow2,
                    "Universal Light Cookie Atlas",
                    false); // support mips, use Pow2Atlas
            }


            m_AdditionalLightsCookieShaderData = new LightCookieShaderData(size, m_Settings.useStructuredBuffer);
        }

        bool isInitialized() => m_AdditionalLightsCookieAtlas != null && m_AdditionalLightsCookieShaderData != null;

        public void Dispose()
        {
            m_AdditionalLightsCookieAtlas?.Release();
            m_AdditionalLightsCookieShaderData?.Dispose();
        }

        public void Setup(ScriptableRenderContext ctx, CommandBuffer cmd, in LightData lightData)
        {
            using var profScope = new ProfilingScope(cmd, ProfilingSampler.Get(URPProfileId.LightCookies));

            // Main light, 1 directional, bound directly
            bool isMainLightAvailable = lightData.mainLightIndex >= 0;
            if (isMainLightAvailable)
                isMainLightAvailable = SetupMainLight(cmd, lightData.visibleLights[lightData.mainLightIndex]);

            // Additional lights, N spot and point lights in atlas
            bool isAdditionalLightsAvailable = lightData.additionalLightsCount > 0;
            if (isAdditionalLightsAvailable)
                isAdditionalLightsAvailable = SetupAdditionalLights(cmd, lightData);

            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightCookie, isMainLightAvailable);
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.AdditionalLightCookies, isAdditionalLightsAvailable);
        }

        bool SetupMainLight(CommandBuffer cmd, in VisibleLight visibleMainLight)
        {
            var mainLight                 = visibleMainLight.light;
            var cookieTexture             = mainLight.cookie;
            bool isMainLightCookieEnabled = cookieTexture != null;

            if (isMainLightCookieEnabled)
            {
                Matrix4x4 cookieMatrix = visibleMainLight.localToWorldMatrix.inverse;
                Vector2 cookieUVScale  = Vector2.one;
                float cookieFormat     = ((cookieTexture as Texture2D)?.format == TextureFormat.Alpha8) ? 1.0f : 0.0f;

                // TODO: verify against HDRP if scale should actually be invScale
                var additionalLightData = mainLight.GetComponent<UniversalAdditionalLightData>();
                if (additionalLightData != null)
                    cookieUVScale = additionalLightData.lightCookieSize;

                cmd.SetGlobalTexture(ShaderProperty._MainLightTexture,       cookieTexture);
                cmd.SetGlobalMatrix(ShaderProperty._MainLightWorldToLight,  cookieMatrix);
                cmd.SetGlobalVector(ShaderProperty._MainLightCookieUVScale, cookieUVScale);
                cmd.SetGlobalFloat(ShaderProperty._MainLightCookieFormat,  cookieFormat);

                //DrawDebugFrustum(visibleMainLight.localToWorldMatrix);
            }

            return isMainLightCookieEnabled;
        }

        bool SetupAdditionalLights(CommandBuffer cmd, in LightData lightData)
        {
            // TODO: better to use growing arrays instead of native arrays, List<T> at interface
            // TODO: how fast is temp alloc???
            var validLights = new NativeArray<LightCookieData>(lightData.additionalLightsCount , Allocator.Temp);
            int validLightCount = PrepareAndValidateAdditionalLights(lightData, ref validLights);

            // Early exit if no valid cookie lights
            if (validLightCount <= 0)
            {
                validLights.Dispose();
                return false;
            }

            // Sort by priority
            unsafe
            {
                CoreUnsafeUtils.QuickSort<LightCookieData>(validLightCount, validLights.GetUnsafePtr());
            }

            // Lazy init GPU resources
            if (validLightCount > 0 && !isInitialized())
                InitAdditionalLights(validLightCount);

            // Update Atlas
            var validSortedLights = validLights.GetSubArray(0, validLightCount);
            var uvRects = new NativeArray<Vector4>(validLightCount , Allocator.Temp);
            int validUVRectCount = UpdateAdditionalLightsAtlas(cmd, lightData, validSortedLights, ref uvRects);

            // Upload shader data
            var validUvRects = uvRects.GetSubArray(0, validUVRectCount);
            UploadAdditionalLights(cmd, lightData, validSortedLights, validUvRects);

            bool isAdditionalLightsEnabled = validUvRects.Length > 0;

            uvRects.Dispose();
            validLights.Dispose();

            return isAdditionalLightsEnabled;
        }

        int PrepareAndValidateAdditionalLights(in LightData lightData, ref NativeArray<LightCookieData> validLights)
        {
            int skipMainLightIndex = lightData.mainLightIndex;
            int lightBufferOffset = 0;
            int validLightCount = 0;
            for (int i = 0; i < lightData.visibleLights.Length; i++)
            {
                if (i == skipMainLightIndex)
                {
                    lightBufferOffset = -1;
                    continue;
                }

                Light light = lightData.visibleLights[i].light;

                // Skip lights without a cookie texture
                if (light.cookie == null)
                    continue;

                // Skip vertex lights, no support
                if (light.renderMode == LightRenderMode.ForceVertex)
                    continue;

                //DrawDebugFrustum(lightData.visibleLights[i].localToWorldMatrix);

                Debug.Assert(i < ushort.MaxValue);

                LightCookieData lp;
                lp.visibleLightIndex = (ushort)i;
                lp.lightBufferIndex  = (ushort)(i + lightBufferOffset);
                lp.priority = 0;
                lp.score = 0;

                // Get user priority
                var additionalLightData = light.GetComponent<UniversalAdditionalLightData>();
                if (additionalLightData != null)
                    lp.priority = additionalLightData.priority;

                // TODO: could be computed globally and shared between systems!
                // Compute automatic importance score
                // Factors:
                // 1. Light screen area
                // 2. Light intensity
                // 4. TODO: better criteria?? spot > point?
                // TODO: Is screen rect accurate? If not then just use size
                Rect  lightScreenUVRect = lightData.visibleLights[i].screenRect;
                float lightScreenAreaUV = lightScreenUVRect.width * lightScreenUVRect.height;
                float lightIntensity    = light.intensity;
                lp.score                = lightScreenAreaUV * lightIntensity;

                validLights[validLightCount++] = lp;
            }

            return validLightCount;
        }

        int UpdateAdditionalLightsAtlas(CommandBuffer cmd, in LightData lightData, in NativeArray<LightCookieData> sortedLights, ref NativeArray<Vector4> textureAtlasUVRects)
        {
            // Test if a texture is in atlas
            // If yes
            //  --> add UV rect
            // If no
            //    --> add into atlas
            //      If no space
            //          --> clear atlas
            //          --> re-insert in priority order
            //          --> TODO: add partial eviction mechanism??
            //          If no space
            //              --> warn
            //          If space
            //              --> add UV rect
            //      If space
            //          --> add UV rect
            bool atlasResetBefore = false;
            int uvRectCount = 0;
            for (int i = 0; i < sortedLights.Length; i++)
            {
                var lcd = sortedLights[i];
                Light light = lightData.visibleLights[lcd.visibleLightIndex].light;
                Texture cookie = light.cookie;

                // TODO: blit point light into octahedraQuad or 2d slices.
                // TODO: blit format convert into A8 or into sRGB
                Vector4 uvScaleOffset = Vector4.zero;
                if (cookie.dimension == TextureDimension.Cube)
                {
                    Debug.Assert(light.type == LightType.Point);
                    uvScaleOffset = FetchCube(cmd, cookie);
                }
                else
                {
                    Debug.Assert(light.type == LightType.Spot);
                    uvScaleOffset = Fetch2D(cmd, cookie);
                }

                bool isCached = uvScaleOffset != Vector4.zero;
                if (!isCached)
                {
                    // Update data
                    //if (m_CookieAtlas.NeedsUpdate(cookie, false))
                    //    m_CookieAtlas.BlitTexture(cmd, scaleBias, cookie, new Vector4(1, 1, 0, 0), blitMips: false);

                    if (atlasResetBefore)
                    {
                        // TODO: better messages
                        //Debug.LogError("Universal Light Cookie Manager: Atlas full!");
                        return uvRectCount;
                    }

                    // Clear atlas allocs
                    m_AdditionalLightsCookieAtlas.ResetAllocator();

                    // Try to reinsert in priority order
                    i = 0;
                    uvRectCount = 0;
                    atlasResetBefore = true;
                    continue;
                }

                textureAtlasUVRects[uvRectCount++] = new Vector4(uvScaleOffset.z, uvScaleOffset.w, uvScaleOffset.x, uvScaleOffset.y); // Flip ( scale, offset) into a rect i.e. ( offset, scale )
            }

            return uvRectCount;
        }

        Vector4 Fetch2D(CommandBuffer cmd, Texture cookie)
        {
            Debug.Assert(cookie != null);
            Debug.Assert(cookie.dimension == TextureDimension.Tex2D);

            Vector4 uvScaleOffset = Vector4.zero;
            m_AdditionalLightsCookieAtlas.AddTexture(cmd, ref uvScaleOffset, cookie);
            m_AdditionalLightsCookieAtlas.UpdateTexture(cmd, cookie, ref uvScaleOffset, cookie);
            return uvScaleOffset;
        }

        int ComputeOctahedralCookieSize(Texture cookie)
        {
            // Map 6*WxH pixels into 2W*2H pixels, so 4/6 ratio or 66% of cube pixels.
            int octCookieSize = Math.Max(cookie.width, cookie.height);
            if (m_Settings.atlas.isPow2)
                octCookieSize = octCookieSize * Mathf.NextPowerOfTwo((int)m_Settings.cubeOctahedralSizeScale);
            else
                octCookieSize = (int)(octCookieSize * m_Settings.cubeOctahedralSizeScale + 0.5f);
            return octCookieSize;
        }

        public Vector4 FetchCube(CommandBuffer cmd, Texture cookie)
        {
            Debug.Assert(cookie != null);
            Debug.Assert(cookie.dimension == TextureDimension.Cube);

            Vector4 uvScaleOffset = Vector4.zero;

            // Check if texture is present
            bool isCached = m_AdditionalLightsCookieAtlas.IsCached(out uvScaleOffset, cookie);
            if (isCached)
            {
                // Update contents if required
                m_AdditionalLightsCookieAtlas.UpdateTexture(cmd, cookie, ref uvScaleOffset);

                return uvScaleOffset;
            }

            // Scale octahedral projection, so that cube -> oct2D pixel count match better.
            int octCookieSize = ComputeOctahedralCookieSize(cookie);

            // Allocate new
            bool isAllocated = m_AdditionalLightsCookieAtlas.AllocateTexture(cmd, ref uvScaleOffset, cookie, octCookieSize, octCookieSize);

            if (isAllocated)
                return uvScaleOffset;

            return Vector4.zero;
        }

        void DrawDebugFrustum(Matrix4x4 m, float near = 1, float far = -1)
        {
            var src = new Vector4[]
            {
                new Vector4(-1, -1, near, 1),
                new Vector4(1, -1, near , 1),
                new Vector4(1, 1, near  , 1),
                new Vector4(-1, 1, near , 1),

                new Vector4(-1, -1, far , 1),
                new Vector4(1, -1, far  , 1),
                new Vector4(1, 1, far   , 1),
                new Vector4(-1, 1, far  , 1),
            };
            var res = new Vector4[8];
            for (int i = 0; i < src.Length; i++)
                res[i] = m * src[i];

            for (int i = 0; i < src.Length; i++)
                res[i] = res[i].w != 0 ? res[i] / res[i].w : res[i];

            Debug.DrawLine(res[0], res[1], Color.black);
            Debug.DrawLine(res[1], res[2], Color.black);
            Debug.DrawLine(res[2], res[3], Color.black);
            Debug.DrawLine(res[3], res[0], Color.black);

            Debug.DrawLine(res[4 + 0], res[4 + 1], Color.white);
            Debug.DrawLine(res[4 + 1], res[4 + 2], Color.white);
            Debug.DrawLine(res[4 + 2], res[4 + 3], Color.white);
            Debug.DrawLine(res[4 + 3], res[4 + 0], Color.white);

            Debug.DrawLine(res[0], res[4 + 0], Color.yellow);
            Debug.DrawLine(res[1], res[4 + 1], Color.yellow);
            Debug.DrawLine(res[2], res[4 + 2], Color.yellow);
            Debug.DrawLine(res[3], res[4 + 3], Color.yellow);

            var o = m * new Vector4(0, 0, 0, 1);
            var x = m * new Vector4(1, 0, 0, 1);
            var y = m * new Vector4(0, 1, 0, 1);
            var z = m * new Vector4(0, 0, 1, 1);
            o = o.w > 0 ? o / o.w : o;
            x = x.w > 0 ? x / x.w : x;
            y = y.w > 0 ? y / y.w : y;
            z = z.w > 0 ? z / z.w : z;
            Debug.DrawLine(o, x, Color.red);
            Debug.DrawLine(o, y, Color.green);
            Debug.DrawLine(o, z, Color.blue);
        }

        void UploadAdditionalLights(CommandBuffer cmd, in LightData lightData, in NativeArray<LightCookieData> validSortedLights, in NativeArray<Vector4> validUvRects)
        {
            Debug.Assert(m_AdditionalLightsCookieAtlas != null);
            Debug.Assert(m_AdditionalLightsCookieShaderData != null);

            float cookieAtlasFormat = (GraphicsFormatUtility.GetTextureFormat(m_AdditionalLightsCookieAtlas.AtlasTexture.rt.graphicsFormat) == TextureFormat.Alpha8) ? 1.0f : 0.0f;
            cmd.SetGlobalTexture(ShaderProperty._AdditionalLightsCookieAtlasTexture, m_AdditionalLightsCookieAtlas.AtlasTexture);
            cmd.SetGlobalFloat(ShaderProperty._AdditionalLightsCookieAtlasFormat, cookieAtlasFormat);

            // TODO: resize for uniform buffer
            m_AdditionalLightsCookieShaderData.Resize(m_Settings.maxAdditionalLights);

            var worldToLights = m_AdditionalLightsCookieShaderData.worldToLights;
            var atlasUVRects = m_AdditionalLightsCookieShaderData.atlasUVRects;
            var lightTypes = m_AdditionalLightsCookieShaderData.lightTypes;

            // TODO: clear enable bits instead
            // Set all rects to Invalid (Vector4.zero).
            Array.Clear(atlasUVRects, 0, atlasUVRects.Length);

            // Fill shader data
            for (int i = 0; i < validUvRects.Length; i++)
            {
                int visIndex = validSortedLights[i].visibleLightIndex;
                int bufIndex = validSortedLights[i].lightBufferIndex;

                lightTypes[bufIndex]    = (int)lightData.visibleLights[visIndex].lightType;
                worldToLights[bufIndex] = lightData.visibleLights[visIndex].localToWorldMatrix.inverse;
                atlasUVRects[bufIndex]  = validUvRects[i];

                // Spot projection
                if (lightData.visibleLights[visIndex].lightType == LightType.Spot)
                {
                    // VisibleLight.localToWorldMatrix only contains position & rotation.
                    // Multiply projection for spot light.
                    float spotAngle = lightData.visibleLights[visIndex].spotAngle;
                    float spotRange = lightData.visibleLights[visIndex].range;
                    var perp = Matrix4x4.Perspective(spotAngle, 1, 0.001f, spotRange);

                    // Cancel embedded camera view axis flip (https://docs.unity3d.com/2021.1/Documentation/ScriptReference/Matrix4x4.Perspective.html)
                    perp.SetColumn(2, perp.GetColumn(2) * -1);

                    // world -> light local -> light perspective
                    worldToLights[bufIndex] = perp * worldToLights[bufIndex];
                }
            }

            // Apply changes and upload to GPU
            m_AdditionalLightsCookieShaderData.Apply(cmd);
        }
    }
}
