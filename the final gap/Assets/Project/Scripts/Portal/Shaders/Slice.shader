Shader "Custom/Slice"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0

        // 核心切片参数：必须与 C# 脚本中的字符串完全一致
        [HideInInspector] sliceNormal("normal", Vector) = (0,0,0,0)
        [HideInInspector] sliceCentre ("centre", Vector) = (0,0,0,0)
        [HideInInspector] sliceOffsetDst("offset", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" "Queue"="Geometry" }
        LOD 200

        // ------------------------------------------------------------------
        // Pass 1: Forward Lit (基础渲染)
        // ------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1; // 传递世界坐标用于切片计算
                float3 worldNormal : NORMAL;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _MainTex_ST;
                half _Glossiness;
                half _Metallic;
                // 对应 PortalTraveller.cs 中的 SetVector/SetFloat 
                float3 sliceNormal;
                float3 sliceCentre;
                float sliceOffsetDst;
            CBUFFER_END

            Varyings vert (Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.worldPos = vertexInput.positionWS;
                output.uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                output.worldNormal = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            // 核心裁剪函数：复刻原代码逻辑
            void ApplySlice(float3 worldPos)
            {
                float3 adjustedCentre = sliceCentre + sliceNormal * sliceOffsetDst;
                float3 offsetToSliceCentre = adjustedCentre - worldPos;
                clip(dot(offsetToSliceCentre, sliceNormal));
            }

            half4 frag (Varyings input) : SV_Target
            {
                // 执行裁剪
                ApplySlice(input.worldPos);

                // 基础 PBR 光照模拟 (为了演示效果，使用简易 Lit 逻辑)
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _Color;
                
                // 获取主光源信息
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(input.worldNormal, mainLight.direction));
                half3 finalColor = color.rgb * (ndotl * mainLight.color + 0.2); // 0.2 为基础环境光

                return half4(finalColor, color.a);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Pass 2: ShadowCaster (阴影裁剪：防止传送门外出现物体阴影)
        // ------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float3 sliceNormal;
                float3 sliceCentre;
                float sliceOffsetDst;
            CBUFFER_END

            Varyings vert (Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.worldPos = vertexInput.positionWS;
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                // 在阴影 Pass 中也必须执行裁剪 
                float3 adjustedCentre = sliceCentre + sliceNormal * sliceOffsetDst;
                float3 offsetToSliceCentre = adjustedCentre - input.worldPos;
                clip(dot(offsetToSliceCentre, sliceNormal));
                
                return 0;
            }
            ENDHLSL
        }
    }
}