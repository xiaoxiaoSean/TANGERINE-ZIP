// Increase the stroke width of near-white pixels without drawing shifted copies.
// The sampling distance becomes zero at the edge of the pointer's radius.
sampler2D scene : register(s0);
float2 cursor : register(c0);
float2 viewport : register(c1);
float radius : register(c2);

float WhiteCoverage(float4 sampleColor)
{
    float low = min(sampleColor.r, min(sampleColor.g, sampleColor.b));
    // Requiring all three channels to be bright also rejects orange accents.
    return saturate((low - 0.38) * 1.75);
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 original = tex2D(scene, uv);
    float distanceFromCursor = length((uv - cursor) * viewport);
    float proximity = saturate(1.0 - distanceFromCursor / radius);

    // At most one device-independent pixel is added at the center. A sub-pixel
    // offset and linear texture sampling keep the new stroke connected to it.
    float2 strokeOffset = (0.95 * proximity) / viewport;
    float sourceCoverage = WhiteCoverage(original);
    float expandedCoverage = WhiteCoverage(tex2D(scene, uv + float2(strokeOffset.x, 0)));
    expandedCoverage = max(expandedCoverage, WhiteCoverage(tex2D(scene, uv - float2(strokeOffset.x, 0))));
    expandedCoverage = max(expandedCoverage, WhiteCoverage(tex2D(scene, uv + float2(0, strokeOffset.y))));
    expandedCoverage = max(expandedCoverage, WhiteCoverage(tex2D(scene, uv - float2(0, strokeOffset.y))));

    // Add only coverage that was absent in the original pixel. Copying the
    // neighboring RGB values created a visible second image in the old shader.
    float addedCoverage = max(0.0, expandedCoverage - sourceCoverage);
    float3 thickened = original.rgb + (1.0 - original.rgb) * addedCoverage;
    return float4(thickened, original.a);
}
