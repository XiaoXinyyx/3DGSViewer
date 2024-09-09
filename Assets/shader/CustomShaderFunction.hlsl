#ifndef CUSTOMSHADERFUNCTION_INCLUDED
#define CUSTOMSHADERFUNCTION_INCLUDED

StructuredBuffer<float4x4> _ObjectToWorldBuffer;

float3 TransformObjectToWorldIndirect(float3 positionOS, uint instanceID)
{
#if defined(SHADER_STAGE_RAY_TRACING)
	return 0; // Not supported
#else
	return mul(_ObjectToWorldBuffer[instanceID], float4(positionOS, 1.0)).xyz;
#endif
}

VertexPositionInputs GetVertexPositionInputsIndirect(float3 positionOS, uint instanceID)
{
	VertexPositionInputs input;
	input.positionWS = TransformObjectToWorldIndirect(positionOS, instanceID);
	input.positionVS = TransformWorldToView(input.positionWS);
	input.positionCS = TransformWorldToHClip(input.positionWS);

	float4 ndc = input.positionCS * 0.5f;
	input.positionNDC.xy = float2(ndc.x, ndc.y * _ProjectionParams.x) + ndc.w;
	input.positionNDC.zw = input.positionCS.zw;

	return input;
}

VertexPositionInputs TransformVertexPositions(float3 positionOS)
{
	VertexPositionInputs input;
	input.positionWS = TransformObjectToWorld(positionOS);
	input.positionVS = TransformWorldToView(input.positionWS);
	input.positionCS = TransformWorldToHClip(input.positionWS);

	float4 ndc = input.positionCS * 0.5f;
	input.positionNDC.xy = float2(ndc.x, ndc.y * _ProjectionParams.x) + ndc.w;
	input.positionNDC.zw = input.positionCS.zw;

	return input;
}

// 计算非均匀缩放的法线变换矩阵
float3x3 GetNormalTransformMatrix(float3x3 Matrix, float3 scale)
{
	float3x3 scaleInverseSquared = float3x3(
		1.0 / (scale.x * scale.x), 0.0, 0.0,
		0.0, 1.0 / (scale.y * scale.y), 0.0,
		0.0, 0.0, 1.0 / (scale.z * scale.z)
	);

	float3x3 normalMatrix = mul(Matrix, scaleInverseSquared);

	return normalMatrix;
}

float Dither8x8Random_float(float2 ScreenPosition, uint2 RandomOffset)
{
	// RandomOffset : random value to offset the dithering pattern in pixel space
	//                Must be greater than 0

	// Pixel position in screen space
	float2 uv = ScreenPosition.xy * _ScreenParams.xy;

	// Modified on the base of https://blog.csdn.net/o83290102o5/article/details/120604171
	const float DITHER_THRESHOLDS[64] =
	{
		 1.0 / 65.0, 49.0 / 65.0, 13.0 / 65.0, 61.0 / 65.0,  4.0 / 65.0, 52.0 / 65.0, 16.0 / 65.0, 64.0 / 65.0,
		33.0 / 65.0, 17.0 / 65.0, 45.0 / 65.0, 29.0 / 65.0, 36.0 / 65.0, 20.0 / 65.0, 48.0 / 65.0, 32.0 / 65.0,
		 9.0 / 65.0, 57.0 / 65.0,  5.0 / 65.0, 53.0 / 65.0, 12.0 / 65.0, 60.0 / 65.0,  8.0 / 65.0, 56.0 / 65.0,
		41.0 / 65.0, 25.0 / 65.0, 37.0 / 65.0, 21.0 / 65.0, 44.0 / 65.0, 28.0 / 65.0, 40.0 / 65.0, 24.0 / 65.0,
		 3.0 / 65.0, 51.0 / 65.0, 15.0 / 65.0, 63.0 / 65.0,  2.0 / 65.0, 50.0 / 65.0, 14.0 / 65.0, 62.0 / 65.0,
		35.0 / 65.0, 19.0 / 65.0, 47.0 / 65.0, 31.0 / 65.0, 34.0 / 65.0, 18.0 / 65.0, 46.0 / 65.0, 30.0 / 65.0,
		11.0 / 65.0, 59.0 / 65.0,  7.0 / 65.0, 55.0 / 65.0, 10.0 / 65.0, 58.0 / 65.0,  6.0 / 65.0, 54.0 / 65.0,
		43.0 / 65.0, 27.0 / 65.0, 39.0 / 65.0, 23.0 / 65.0, 42.0 / 65.0, 26.0 / 65.0, 38.0 / 65.0, 22.0 / 65.0
	};

	uint index = ((uint(uv.x) + RandomOffset.x) % 8) * 8 + (uint(uv.y) + RandomOffset.y) % 8;
	return DITHER_THRESHOLDS[index]; // Alpha clip
}

#endif