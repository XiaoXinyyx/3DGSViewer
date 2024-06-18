Shader "3DGS/GaussianShader"
{
    Properties
    {
        //_MainTex ("Texture", 2D) = "white" {}
        
        // hide in inspector

        [HideInInspector]_PackedData0 ("Packed Data 0", Vector) = (0, 0, 1, 0)
        [HideInInspector]_PackedData1 ("Packed Data 1", Vector) = (0.5, 0.5, 0.5, 0)

        _ScaleModifier ("Scale Modifier", Range(1.0, 5.0)) = 4.0
        _DebugPointSize ("Debug Point Radius", Range(0.0, 0.08)) = 0.0027
        _AlphaClipThresholdMin ("Alpha Clip Threshold Min", Range(0.0, 1.0)) = 0.0
        _AlphaClipThresholdMax ("Alpha Clip Threshold Max", Range(0.0, 1.0)) = 1.0

        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull [_Cull]
        ZTest LEqual
        ZWrite On
        LOD 200

        Pass
        {
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile_instancing // GPU instancing
            #pragma instancing_options assumeuniformscaling

            //#include "UnityCG.cginc"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "CustomShaderFunction.hlsl"

            struct appdata
            {
                //float2 uv : TEXCOORD0;
                float4 vertex  : POSITION;
                float3 normal  : NORMAL;
                float4 tangent : TANGENT;
                UNITY_VERTEX_INPUT_INSTANCE_ID // Instance ID
            };

            struct v2f
            {
                // float2 uv : TEXCOORD0;
                float4 vertex   : SV_POSITION;
                float4 posNDC   : TEXCOORD0;
                float3 posWS    : TEXCOORD1;
                float4 tangent  : TEXCOORD2;
                float3 binormal : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID // Instance ID
            };

            UNITY_INSTANCING_BUFFER_START(Props)
                // randomValue, vertex.opacity, ,0
                UNITY_DEFINE_INSTANCED_PROP(float4, _PackedData0)
                // vertex.f_dc.x, vertex.f_dc.y, vertex.f_dc.z, 0
                UNITY_DEFINE_INSTANCED_PROP(float4, _PackedData1)
            UNITY_INSTANCING_BUFFER_END(Props)

            sampler2D _MainTex;
            float4 _MainTex_ST;

            float _ScaleModifier;
            float _DebugPointSize;
            float _AlphaClipThresholdMin;
            float _AlphaClipThresholdMax;

            v2f vert (appdata v)
            {
                v2f output;

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, output);
                
                v.vertex.xyz *= _ScaleModifier;

                // Position transform
                VertexPositionInputs packedPos = GetVertexPositionInputs(v.vertex.xyz);
                
                // Normal, tangent transform
                float3 worldNormal = TransformObjectToWorldDir(v.normal.xyz, false);
                float3 worldTangent = TransformObjectToWorldDir(v.tangent.xyz, false);
                float3 worldBinormal = cross(worldNormal, worldTangent) * v.tangent.w;

                output.vertex = packedPos.positionCS;
                output.posNDC = packedPos.positionNDC;
                output.posWS = packedPos.positionWS;
                output.tangent = float4(worldTangent, v.tangent.w);
                output.binormal = worldBinormal;

                //o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return output;
            }

            float4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                
                // Object orginal point in world space
                float3 originPosWS = GetObjectToWorldMatrix()._m03_m13_m23;

                // Unpack per instance data
                float4 data0 = UNITY_ACCESS_INSTANCED_PROP(Props, _PackedData0);
                float4 data1 = UNITY_ACCESS_INSTANCED_PROP(Props, _PackedData1);
                uint randomValue = uint(data0.x);
                float3 minAxisEndWS = float3(data0.z, data0.w, data1.w) * _ScaleModifier
                                      + originPosWS;
                #if defined(UNITY_INSTANCING_ENABLED)   // Alpha
                    float alpha = data0.y;
                #else
                    float alpha = 1.0;
                #endif

                // Initialze color
                float4 col = float4(0.5, 0.5, 0.5, 1);

                // Dither transparent
                float2 screenPos = i.posNDC.xy / i.posNDC.w; // Screen Position
                uint2 offset = uint2(randomValue / 8, randomValue % 8);
                float alphaClip = Dither8x8Random_float(screenPos, offset);
                clip(min(min(
                    alpha - alphaClip,
                    alpha - _AlphaClipThresholdMin),
                    _AlphaClipThresholdMax - alpha)
                    );

                // View dir
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(i.posWS.xyz);
                // Sphere direction vector in world space
                float3 sphereDirWS = normalize(i.posWS.xyz - originPosWS.xyz);
                // World space normal
                // TODO: better normal
                float3 worldNormal = normalize(cross(i.tangent.xyz, i.binormal.xyz) * i.tangent.w);

                // Simple Lighting
                // TODO: Better lighting
                float3 ambient = float3(0.2, 0.2, 0.2); // Simple ambient
                Light light = GetMainLight();
                float3 directLighting = LightingLambert(light.color, light.direction, worldNormal);
                float3 ligthing = ambient + directLighting;

                // DEBUG
                float debugValue = saturate( 
                    step(length(i.posWS.xyz - minAxisEndWS), _DebugPointSize)
                    + step(length(i.posWS.xyz + minAxisEndWS - 2.0 * originPosWS), _DebugPointSize));                
                float3 debugColor = float3(0, 0.5, 0.5);
                float3 debug = debugColor * debugValue;

                // Combine color
                col.rgb = ligthing + debug;
                return col;
            }
            ENDHLSL
        } // End pass
    }
}
