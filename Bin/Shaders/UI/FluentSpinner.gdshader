shader_type canvas_item;

uniform vec4 primary_color : source_color = vec4(0.0, 0.46, 1.0, 1.0); // 主色
uniform vec4 track_color : source_color = vec4(0.85, 0.85, 0.85, 0.3); // 轨道背景色
uniform float animation_speed : hint_range(0.5, 2.0) = 1.0;

// 动画阶段定义
const float TOTAL_DURATION = 1.8;
const float PHASE0_DURATION = 0.5;
const float PHASE1_DURATION = 0.4;
const float PHASE2_DURATION = 0.5;
const float PHASE3_DURATION = 0.4;
const float PI2 = PI * 2.0;

void fragment() {
    // 中心化UV坐标 (-0.5 到 0.5)
    vec2 uv = UV - 0.5;
    float radius = length(uv);

    // 当前动画时间 (0-1.3秒循环)
    float t = mod(TIME * animation_speed, TOTAL_DURATION);

    // 初始化弧段参数 (0°在顶部)
    float head_angle = 0.0;
    float tail_angle = 0.0;

    // 根据时间计算弧段参数 (20°弧长)
    if (t < PHASE0_DURATION) { // 阶段0: 0.0-0.2s
        float phase_t = smoothstep(0.0, 1.0, t / PHASE0_DURATION);
        // 头尾同步运动：头20°→110°，尾0°→90°
        head_angle = radians(20.0 + 90.0 * phase_t); // 20° → 110°
        tail_angle = radians(0.0 + 90.0 * phase_t);   // 0° → 90°
    } else if (t < PHASE0_DURATION + PHASE1_DURATION) { // 阶段1: 0.2-0.3s
        // 阶段1开始时，尾刚好到达90°，头刚好到达110°
        // 头从110°到360°，尾保持在90°
        float phase_t = smoothstep(0.0, 1.0, (t - PHASE0_DURATION) / PHASE1_DURATION);
        head_angle = radians(110.0 + 250.0 * phase_t); // 110° → 360°
        tail_angle = radians(90.0);                    // 90° (保持不变)
    } else if (t < PHASE0_DURATION + PHASE1_DURATION + PHASE2_DURATION) { // 阶段2: 0.3-0.8s
        // 阶段2开始时，尾在90°，头在360°
        float phase_t = smoothstep(0.0, 1.0, (t - (PHASE0_DURATION + PHASE1_DURATION)) / PHASE2_DURATION);
        head_angle = radians(360.0 + 270.0 * phase_t); // 360° → 630°
        tail_angle = radians(90.0 + 450.0 * phase_t);   // 90° → 540°
    } else { // 阶段3: 0.8-1.3s
        // 阶段3开始时，头在630°，尾在540°
        float phase_t = smoothstep(0.0, 1.0, (t - (PHASE0_DURATION + PHASE1_DURATION + PHASE2_DURATION)) / PHASE3_DURATION);
        head_angle = radians(630.0 + 110.0 * phase_t); // 630° → 740°(≡20°)
        tail_angle = radians(540.0 + 180.0 * phase_t);   // 540° → 720°(≡0°)
    }

    // 归一化角度到0-2π范围
    head_angle = mod(head_angle, PI2);
    tail_angle = mod(tail_angle, PI2);

    // 计算当前像素的角度位置 (0°在顶部)
    float pixel_angle = mod(atan(uv.x, -uv.y), PI2);

    // 计算径向距离因子 (确保在环内)
    float radial_factor = smoothstep(0.35, 0.4, radius) * (1.0 - smoothstep(0.45, 0.5, radius));

    // 1. 绘制轨道背景 (整个环形轨道)
    vec4 final_color = vec4(track_color.rgb, track_color.a * radial_factor);

    // 2. 绘制活动弧段
    bool in_arc = false;
    if (tail_angle < head_angle) {
        in_arc = (pixel_angle >= tail_angle && pixel_angle <= head_angle);
    } else {
        in_arc = (pixel_angle >= tail_angle || pixel_angle <= head_angle);
    }

    if (in_arc && radial_factor > 0.0) {
        // 覆盖轨道背景，显示活动弧段
        final_color = vec4(primary_color.rgb, primary_color.a * radial_factor);
    }

    // 输出最终颜色
    COLOR = final_color;
}