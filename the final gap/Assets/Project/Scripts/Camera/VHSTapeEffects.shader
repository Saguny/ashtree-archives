Shader "HoL/VHSTapeEffects"
{
    Properties
    {
        [Header(Lens)]
        _BarrelStrength     ("Barrel Distortion",       Range(0, 0.3))   = 0.08

        [Header(Chromatic Aberration)]
        _ChromaticStrength  ("Chromatic Strength",      Range(0, 0.008)) = 0.0018

        [Header(Scanline Band)]
        _BandDistortion     ("Band Distortion Amount",  Range(0, 0.06))  = 0.025

        [Header(Color Bleed)]
        _BleedAmount        ("Color Bleed",             Range(0, 1))     = 0.5

        [Header(Grain)]
        _GrainIntensity     ("Grain Intensity",         Range(0, 1))     = 0.18
        _GrainScale         ("Grain Scale",             Float)           = 1.0

        [Header(Glitch Video Overlay)]
        _GlitchIntensity    ("Glitch Overlay Strength", Range(0, 2))    = 1.0
        _GlitchScale        ("Glitch UV Scale",         Range(0.1, 4))  = 1.0

        [Header(Mode)]
        [Enum(Procedural,0,Video RT,1)]
        _VHSMode            ("VHS Mode",                Float)          = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "VHS_FoundFootage"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _BarrelStrength;
            float _ChromaticStrength;
            float _BandDistortion;
            float _BleedAmount;
            float _GrainIntensity;
            float _GrainScale;
            float _GlitchIntensity;
            float _GlitchScale;
            float _VHSMode;

            TEXTURE2D(_GlitchTex);
            SAMPLER(sampler_GlitchTex);

            // Set by VHSTapeEffectController every frame
            float _VHSIntensity;
            float _VHSScanlineY;   // rolling band Y position (0-1, animated from C#)
            float _VHSScanlineX;   // secondary band position

            // ── Helpers ──────────────────────────────────────────────────────────

            float Hash21(float2 p)
            {
                p = frac(p * float2(0.1031, 0.1030));
                p += dot(p, p.yx + 33.33);
                return frac((p.x + p.y) * p.x);
            }

            float2 BarrelDistort(float2 uv, float strength)
            {
                float2 c = uv - 0.5;
                return uv + c * dot(c, c) * strength;
            }

            // ── Fragment ─────────────────────────────────────────────────────────

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 original = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord, _BlitMipLevel);

                float intensity = _VHSIntensity;
                if (intensity <= 0.001)
                    return original;

                // ── Barrel distortion ───────────────────────────────────────────
                float2 uv = BarrelDistort(input.texcoord, _BarrelStrength);

                if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
                    return lerp(original, half4(0, 0, 0, 1), intensity);

                half4 col;

                if (_VHSMode < 0.5)
                {
                    // ── MODE 0 : PROCEDURAL ──────────────────────────────────────

                    // Rolling scanline band distortion (Puppet Combo approach)
                    float u   = uv.y;
                    float dx  = 1.0 - abs(distance(u, _VHSScanlineX));
                    float dy  = 0.0 - abs(distance(u, _VHSScanlineY));

                    uv.x += dy * _BandDistortion;

                    if ((dx + 0.0001) > 0.99)
                        uv.y = _VHSScanlineX;

                    uv = frac(uv);

                    // Chromatic aberration (horizontal, subtle)
                    float ca = _ChromaticStrength;
                    half r = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, float2(uv.x + ca, uv.y), _BlitMipLevel).r;
                    half g = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv,                      _BlitMipLevel).g;
                    half b = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, float2(uv.x - ca, uv.y), _BlitMipLevel).b;
                    col = half4(r, g, b, 1.0);

                    // Color bleed
                    float bleed = 0;
                    bleed += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + float2(0.010, 0.000), _BlitMipLevel).r;
                    bleed += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv,                        _BlitMipLevel).r;
                    bleed += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + float2(0.000, 0.010), _BlitMipLevel).r;
                    bleed += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + float2(0.000, 0.020), _BlitMipLevel).r;
                    bleed  = (bleed / 4.0) / 50.0;
                    if (bleed > 0.1)
                        col.r += bleed * _BleedAmount * _VHSScanlineY;

                    // Grain at 320x240 grid (~24fps flicker)
                    float gx    = floor(uv.x * 320.0 * _GrainScale) / (320.0 * _GrainScale);
                    float gy    = floor(uv.y * 240.0 * _GrainScale) / (240.0 * _GrainScale);
                    float gTime = floor(_Time.y * 24.0) / 24.0;
                    float grain = Hash21(float2(gx, gy) + gTime);
                    col.rgb -= (grain - 0.5) * _GrainIntensity;
                }
                else
                {
                    // ── MODE 1 : VIDEO RT ONLY ───────────────────────────────────
                    // Start from the original (distorted by barrel, CA still applied)
                    float ca = _ChromaticStrength;
                    half r = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, float2(uv.x + ca, uv.y), _BlitMipLevel).r;
                    half g = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv,                      _BlitMipLevel).g;
                    half b = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, float2(uv.x - ca, uv.y), _BlitMipLevel).b;
                    col = half4(r, g, b, 1.0);

                    // Glitch video overlay (additive — black areas in the RT contribute nothing)
                    float2 glitchUV = (uv - 0.5) / max(_GlitchScale, 0.0001) + 0.5;
                    half4 glitch    = SAMPLE_TEXTURE2D(_GlitchTex, sampler_GlitchTex, glitchUV);
                    col.rgb        += glitch.rgb * _GlitchIntensity;
                }

                return lerp(original, col, intensity);
            }
            ENDHLSL
        }
    }
}
