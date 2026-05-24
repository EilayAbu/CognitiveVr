// Made with Amplify Shader Editor by Alexey Master

Shader "Custom/DigitClockShader"
{
	Properties
	{
		[HideInInspector]_DigitsDays01("DigitsDays01", 2D) = "white" {}
		[HideInInspector]_DigitMask_CL01("DigitMask_CL01", 2D) = "white" {}
		[HideInInspector]_DigitClock_MAP01("DigitClock_MAP01", 2D) = "white" {}
		[HideInInspector]_Digit01("Digit01", 2DArray ) = "" {}
		[HideInInspector]_Digit02("Digit02", 2DArray ) = "" {}
		[HideInInspector]_DigitDots01("DigitDots01", 2D) = "white" {}
		[HideInInspector]_Digit03("Digit03", 2DArray ) = "" {}
		[HideInInspector]_Digit04("Digit04", 2DArray ) = "" {}
		[HideInInspector]_MonthDigit01("MonthDigit01", 2DArray ) = "" {}
		[HideInInspector]_MonthDigit02("MonthDigit02", 2DArray ) = "" {}
		[HideInInspector]_DayDigit01("DayDigit01", 2DArray ) = "" {}
		[HideInInspector]_DayDigit02("DayDigit02", 2DArray ) = "" {}
		[HideInInspector]_Letters_MD01("Letters_MD01", 2D) = "white" {}
		[HideInInspector]_DigitControl01("DigitControl01", Int) = 0
		[HideInInspector]_DigitControl02("DigitControl02", Int) = 0
		[HideInInspector]_DigitControl03("DigitControl03", Int) = 0
		[HideInInspector]_DigitControl04("DigitControl04", Int) = 1
		[HideInInspector]_MonthControl01("MonthControl01", Int) = 0
		[HideInInspector]_MonthControl02("MonthControl02", Int) = 1
		[HideInInspector]_DayControl01("DayControl01", Int) = 0
		[HideInInspector]_DayControl02("DayControl02", Int) = 1
		_ColorCase("Color Case", Color) = (0.745283,0.3553814,0,0)
		_ColorFace("Color Face", Color) = (0.6415094,0,0,0)
		_MetallicCase("Metallic Case", Range( 0 , 1)) = 0
		_SmothnessCase("Smothness Case", Range( 0 , 1)) = 0
		_MetallicFace("Metallic Face", Range( 0 , 1)) = 0.5120001
		_SmoothnessFace("Smoothness Face", Range( 0 , 1)) = 0.199
		_EmissionFace("Emission Face", Range( 0 , 1)) = 0.5
		[HideInInspector]_MaskControlY("MaskControlY", Float) = -1
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	}

	SubShader
	{
		Tags{ "RenderType" = "Opaque"  "Queue" = "Geometry+0" "IsEmissive" = "true"  }
		Cull Back
		CGPROGRAM
		#pragma target 3.5
		#pragma surface surf Standard keepalpha addshadow fullforwardshadows 
		struct Input
		{
			float2 uv_texcoord;
		};

		uniform float4 _ColorCase;
		uniform UNITY_DECLARE_TEX2DARRAY( _Digit01 );
		uniform float4 _Digit01_ST;
		uniform int _DigitControl01;
		uniform UNITY_DECLARE_TEX2DARRAY( _Digit02 );
		uniform float4 _Digit02_ST;
		uniform int _DigitControl02;
		uniform UNITY_DECLARE_TEX2DARRAY( _Digit03 );
		uniform float4 _Digit03_ST;
		uniform int _DigitControl03;
		uniform UNITY_DECLARE_TEX2DARRAY( _Digit04 );
		uniform float4 _Digit04_ST;
		uniform int _DigitControl04;
		uniform sampler2D _DigitsDays01;
		uniform float4 _DigitsDays01_ST;
		uniform sampler2D _DigitMask_CL01;
		uniform float _MaskControlY;
		uniform sampler2D _DigitDots01;
		uniform float4 _DigitDots01_ST;
		uniform UNITY_DECLARE_TEX2DARRAY( _MonthDigit01 );
		uniform float4 _MonthDigit01_ST;
		uniform int _MonthControl01;
		uniform UNITY_DECLARE_TEX2DARRAY( _MonthDigit02 );
		uniform float4 _MonthDigit02_ST;
		uniform int _MonthControl02;
		uniform UNITY_DECLARE_TEX2DARRAY( _DayDigit01 );
		uniform float4 _DayDigit01_ST;
		uniform int _DayControl01;
		uniform UNITY_DECLARE_TEX2DARRAY( _DayDigit02 );
		uniform float4 _DayDigit02_ST;
		uniform int _DayControl02;
		uniform sampler2D _Letters_MD01;
		uniform float4 _Letters_MD01_ST;
		uniform float4 _ColorFace;
		uniform sampler2D _DigitClock_MAP01;
		uniform float4 _DigitClock_MAP01_ST;
		uniform float _EmissionFace;
		uniform float _MetallicCase;
		uniform float _MetallicFace;
		uniform float _SmothnessCase;
		uniform float _SmoothnessFace;

		void surf( Input i , inout SurfaceOutputStandard o )
		{
			float2 uv_Digit01 = i.uv_texcoord * _Digit01_ST.xy + _Digit01_ST.zw;
			float4 texArray1 = UNITY_SAMPLE_TEX2DARRAY(_Digit01, float3(uv_Digit01, (float)_DigitControl01)  );
			float2 uv_Digit02 = i.uv_texcoord * _Digit02_ST.xy + _Digit02_ST.zw;
			float4 texArray7 = UNITY_SAMPLE_TEX2DARRAY(_Digit02, float3(uv_Digit02, (float)_DigitControl02)  );
			float2 uv_Digit03 = i.uv_texcoord * _Digit03_ST.xy + _Digit03_ST.zw;
			float4 texArray9 = UNITY_SAMPLE_TEX2DARRAY(_Digit03, float3(uv_Digit03, (float)_DigitControl03)  );
			float2 uv_Digit04 = i.uv_texcoord * _Digit04_ST.xy + _Digit04_ST.zw;
			float4 texArray11 = UNITY_SAMPLE_TEX2DARRAY(_Digit04, float3(uv_Digit04, (float)_DigitControl04)  );
			float2 uv_DigitsDays01 = i.uv_texcoord * _DigitsDays01_ST.xy + _DigitsDays01_ST.zw;
			float4 tex2DNode16 = tex2D( _DigitsDays01, uv_DigitsDays01 );
			float2 temp_cast_4 = (( 0.0 + _MaskControlY )).xx;
			float2 uv_TexCoord43 = i.uv_texcoord * float2( 1,4 ) + temp_cast_4;
			float4 temp_output_18_0 = ( tex2DNode16 * tex2D( _DigitMask_CL01, uv_TexCoord43 ) );
			float2 uv_DigitDots01 = i.uv_texcoord * _DigitDots01_ST.xy + _DigitDots01_ST.zw;
			float2 uv_MonthDigit01 = i.uv_texcoord * _MonthDigit01_ST.xy + _MonthDigit01_ST.zw;
			float4 texArray19 = UNITY_SAMPLE_TEX2DARRAY(_MonthDigit01, float3(uv_MonthDigit01, (float)_MonthControl01)  );
			float2 uv_MonthDigit02 = i.uv_texcoord * _MonthDigit02_ST.xy + _MonthDigit02_ST.zw;
			float4 texArray21 = UNITY_SAMPLE_TEX2DARRAY(_MonthDigit02, float3(uv_MonthDigit02, (float)_MonthControl02)  );
			float2 uv_DayDigit01 = i.uv_texcoord * _DayDigit01_ST.xy + _DayDigit01_ST.zw;
			float4 texArray25 = UNITY_SAMPLE_TEX2DARRAY(_DayDigit01, float3(uv_DayDigit01, (float)_DayControl01)  );
			float2 uv_DayDigit02 = i.uv_texcoord * _DayDigit02_ST.xy + _DayDigit02_ST.zw;
			float4 texArray24 = UNITY_SAMPLE_TEX2DARRAY(_DayDigit02, float3(uv_DayDigit02, (float)_DayControl02)  );
			float2 uv_Letters_MD01 = i.uv_texcoord * _Letters_MD01_ST.xy + _Letters_MD01_ST.zw;
			float4 lerpResult52 = lerp( ( ( texArray1 + texArray7 + texArray9 + texArray11 + temp_output_18_0 + tex2D( _DigitDots01, uv_DigitDots01 ) + ( texArray19 + texArray21 + texArray25 + texArray24 + tex2D( _Letters_MD01, uv_Letters_MD01 ) ) ) * _ColorFace ) , temp_output_18_0 , tex2DNode16);
			float2 uv_DigitClock_MAP01 = i.uv_texcoord * _DigitClock_MAP01_ST.xy + _DigitClock_MAP01_ST.zw;
			float4 tex2DNode3 = tex2D( _DigitClock_MAP01, uv_DigitClock_MAP01 );
			float4 lerpResult6 = lerp( _ColorCase , lerpResult52 , tex2DNode3.r);
			o.Albedo = lerpResult6.rgb;
			o.Emission = ( lerpResult52 * _EmissionFace ).rgb;
			float lerpResult36 = lerp( _MetallicCase , _MetallicFace , tex2DNode3.r);
			o.Metallic = lerpResult36;
			float lerpResult38 = lerp( _SmothnessCase , _SmoothnessFace , tex2DNode3.r);
			o.Smoothness = lerpResult38;
			o.Alpha = 1;
		}

		ENDCG
	}
	Fallback "Diffuse"	
}
