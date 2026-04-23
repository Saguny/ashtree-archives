Shader "Custom/Portal"
{
    Properties
    {
        // 显式声明主纹理，方便 Inspector 调试，虽然后端由脚本赋值
        _MainTex ("Main Texture", 2D) = "white" {}
        _InactiveColour ("Inactive Color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        // URP 规范：必须指定 RenderPipeline
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            // 引用 URP 核心库 
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 screenPos : TEXCOORD0;
            };

            // URP 纹理定义规范
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // SRP Batcher 兼容：所有属性必须放在 UnityPerMaterial 缓冲区中 
            CBUFFER_START(UnityPerMaterial)
                float4 _InactiveColour;
                int displayMask;
            CBUFFER_END

            v2f vert (appdata v)
            {
                v2f o;
                // 使用 URP 坐标变换宏 
                VertexPositionInputs vertexInput = GetVertexPositionInputs(v.vertex.xyz);
                o.vertex = vertexInput.positionCS; 
                // 计算屏幕坐标 
                o.screenPos = ComputeScreenPos(o.vertex);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                // 透视除法获取 UV [cite: 7]
                float2 uv = i.screenPos.xy / i.screenPos.w;
                // URP 采样宏
                half4 portalCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                
                // 核心逻辑保持不变 [cite: 8]
                return portalCol * displayMask + _InactiveColour * (1 - displayMask);
            }
            ENDHLSL
        }
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}