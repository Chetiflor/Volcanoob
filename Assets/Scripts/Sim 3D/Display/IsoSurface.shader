Shader "IsoSurface" {
    Properties
    {
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
 
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
            #pragma target 4.5
 
            #include "UnityCG.cginc"
 
            StructuredBuffer<float3> Vertices;
            StructuredBuffer<float> Temperatures;
            StructuredBuffer<int> Mask;
 
            struct v2f
            {
                float4 pos : SV_POSITION;
                float temperature : TEXCOORD0;
                float toDraw: TEXCOORD1;
            };

			sampler2D ColourMap;
 
            v2f vert(uint vid : SV_VertexID)
            {
                v2f o;
                // if(Mask[vid/3] == 1)
                // {
                //     float4 pos = float4(Vertices[vid],1);
                //     o.pos = mul(UNITY_MATRIX_VP, float4(pos.xyz, 1));
                //     o.temperature = Temperatures[vid]/1273;
                //     o.toDraw = 1.0f;
                // }
                // else
                // {
                //     o.pos = float4(0,0,0,1);
                //     o.temperature = 0;
                //     o.toDraw = 0.f;
                // }
                o.pos = mul(UNITY_MATRIX_VP,float4(sin(vid*10),sin(vid*200),sin(vid*3000),1));
                o.temperature = Temperatures[vid]/1273;
                //o.temperature = 1000.0f/1273;
                o.toDraw.x = 1.f;
                return o;
            }
 
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 colour = tex2Dlod(ColourMap, float4(i.temperature, 0.5,0,0));
                //colour.w = i.toDraw;
                return colour;
            }
 
            ENDCG
        }
    }
}
