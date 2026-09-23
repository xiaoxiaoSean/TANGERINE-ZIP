// Expand only bright, nearly neutral pixels close to the mouse pointer.
// Dark backgrounds and colored status indicators keep their original color.
sampler2D scene : register(s0);
float2 cursor : register(c0);
float2 viewport : register(c1);
float radius : register(c2);

float3 WhitePixel(float4 sampleColor)
{
    float low = min(sampleColor.r, min(sampleColor.g, sampleColor.b));
    return sampleColor.rgb * step(0.72, low);
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 original = tex2D(scene, uv);
    float2 stepSize = 1.35 / viewport;
    float3 adjacent = WhitePixel(tex2D(scene, uv + float2(stepSize.x, 0)));
    adjacent = max(adjacent, WhitePixel(tex2D(scene, uv - float2(stepSize.x, 0))));
    adjacent = max(adjacent, WhitePixel(tex2D(scene, uv + float2(0, stepSize.y))));
    adjacent = max(adjacent, WhitePixel(tex2D(scene, uv - float2(0, stepSize.y))));
    adjacent = max(adjacent, WhitePixel(tex2D(scene, uv + stepSize)));
    adjacent = max(adjacent, WhitePixel(tex2D(scene, uv - stepSize)));
    adjacent = max(adjacent, WhitePixel(tex2D(scene, uv + float2(stepSize.x, -stepSize.y))));
    adjacent = max(adjacent, WhitePixel(tex2D(scene, uv + float2(-stepSize.x, stepSize.y))));

    float distanceFromCursor = length((uv - cursor) * viewport);
    float influence = saturate((radius - distanceFromCursor) / max(12.0, radius * 0.28));
    return float4(lerp(original.rgb, max(original.rgb, adjacent), influence), original.a);
}
