shader_type canvas_item;

// 正确的屏幕纹理声明
uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;

// 模糊参数
uniform float blur_strength : hint_range(0, 20) = 8.0;
uniform float brightness : hint_range(0, 2) = 0.8;
uniform float saturation : hint_range(0, 2) = 1.2;
uniform float noise_amount : hint_range(0, 0.1) = 0.02;
uniform float opacity : hint_range(0, 1) = 0.85;

// 高斯模糊核 (优化版本)
vec4 blur(sampler2D tex, vec2 uv, float strength) {
    vec4 color = vec4(0);
    float total = 0.0;
    float offset = strength / 1000.0;

    // 使用硬编码偏移提高性能
    vec2 offsets[9] = vec2[](
        vec2(-offset, -offset), vec2(0.0, -offset), vec2(offset, -offset),
        vec2(-offset, 0.0), vec2(0.0, 0.0), vec2(offset, 0.0),
        vec2(-offset, offset), vec2(0.0, offset), vec2(offset, offset)
    );

    float weights[9] = float[](
        0.077847, 0.123317, 0.077847,
        0.123317, 0.195346, 0.123317,
        0.077847, 0.123317, 0.077847
    );

    for (int i = 0; i < 9; i++) {
        color += texture(tex, uv + offsets[i]) * weights[i];
        total += weights[i];
    }

    return color / total;
}

// 优化的噪声函数
float rand(vec2 co) {
    return fract(sin(dot(co.xy, vec2(12.9898, 78.233))) * 43758.5453);
}

void fragment() {
    // 使用正确的屏幕纹理
    vec4 bg = blur(screen_texture, SCREEN_UV, blur_strength);

    // 亮度和饱和度调整 (无需HSV转换)
    vec3 luminance = vec3(0.2126, 0.7152, 0.0722);
    float luma = dot(bg.rgb, luminance);
    vec3 saturated = mix(vec3(luma), bg.rgb, saturation);
    vec3 final_color = saturated * brightness;

    // 添加亚克力噪声
    float noise = rand(FRAGCOORD.xy) * noise_amount;
    final_color += noise;

    // 设置最终颜色和透明度
    COLOR = vec4(final_color, opacity);
}