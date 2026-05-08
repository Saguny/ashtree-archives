Shader "HoL/PSXScreenEffects"
{
    Properties
    {
        [Header(PSX Resolution)]
        _PSXPixelsX ("Pixels X", Float) = 320
        _PSXPixelsY ("Pixels Y", Float) = 240
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "PSX_Pixelate"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _PSXPixelsX;
            float _PSXPixelsY;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                // Guard against unset / zero values
                float px = max(_PSXPixelsX, 1.0);
                float py = max(_PSXPixelsY, 1.0);

                // Snap UV to pixel-center grid at the target resolution.
                // Using sampler_LinearClamp at snapped pixel-center coords gives
                // identical results to point sampling — no interpolation occurs.
                float2 pixelSize = float2(1.0 / px, 1.0 / py);
                uv = floor(uv / pixelSize) * pixelSize + pixelSize * 0.5;

                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, _BlitMipLevel);
            }
            ENDHLSL
        }
    }
}
