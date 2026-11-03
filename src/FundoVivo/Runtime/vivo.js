// Fundo Vivo runtime for .vivo wallpapers (format version 1), WebGL2.
// The whole picture is a pure function of time: draw(t). Live, t comes from the animation frame clock (held to the
// fps limit by the host's shim); rendering, the host asks for each frame by its time and reads the pixels back.
'use strict';
(() => {
  const params = new URLSearchParams(location.search);
  const MODE = params.get('mode') === 'render' ? 'render' : 'live';
  const RENDER_W = +params.get('w') || 0, RENDER_H = +params.get('h') || 0;
  const MARGIN = Math.max(0, +params.get('margin') || 0);   // render only: extra picture per side, for the pan
  const STILL = params.has('still') ? +params.get('still') || 0 : null;   // live, one frame at that time (previews, tests)
  const FAKE_AUDIO = params.has('fakeaudio');   // tests and previews: a made-up spectrum, so what reacts to sound shows
  const host = window.chrome && window.chrome.webview;
  const post = (m) => { try { host && host.postMessage(m); } catch (e) { /* host gone */ } };
  const warnings = [];
  const warn = (m) => { warnings.push(m); console.warn('Fundo Vivo:', m); };

  const canvas = document.getElementById('c');
  const gl = canvas.getContext('webgl2', { alpha: false, antialias: true, premultipliedAlpha: true, preserveDrawingBuffer: false });
  if (!gl) { post({ type: 'error', message: 'WebGL2 indisponível' }); return; }

  // ------------------------------------------------------------------ small helpers
  const TAU = Math.PI * 2;
  const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
  const num = (v, d) => (typeof v === 'number' && isFinite(v) ? v : d);
  function color(hex, fallback) {
    if (typeof hex !== 'string' || hex[0] !== '#') return fallback || [0, 0, 0, 1];
    let h = hex.slice(1);
    if (h.length === 3) h = h.split('').map((c) => c + c).join('');
    if (h.length !== 6 && h.length !== 8) return fallback || [0, 0, 0, 1];
    const v = (i) => parseInt(h.slice(i, i + 2), 16) / 255;
    return [v(0), v(2), v(4), h.length === 8 ? v(6) : 1];
  }
  // deterministic pseudo-random from integers
  function hash(...n) {
    let x = 2166136261;
    for (const v of n) { x ^= Math.floor(v * 1000003) | 0; x = Math.imul(x, 16777619); }
    x ^= x >>> 15; x = Math.imul(x, 2246822507); x ^= x >>> 13;
    return ((x >>> 0) % 1000000) / 1000000;
  }
  // up to n colours as one flat array for a uniform vec4[n]
  function palette(list, n, fallback) {
    const out = new Float32Array(n * 4);
    const cols = (Array.isArray(list) && list.length ? list : [fallback || '#ffffff']).slice(0, n).map((c) => color(c, [1, 1, 1, 1]));
    cols.forEach((c, k) => out.set(c, k * 4));
    return { data: out, count: cols.length };
  }

  // ------------------------------------------------------------------ shaders
  function shader(type, src) {
    const s = gl.createShader(type);
    gl.shaderSource(s, src);
    gl.compileShader(s);
    if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s));
    return s;
  }
  function program(vs, fs) {
    const p = gl.createProgram();
    gl.attachShader(p, shader(gl.VERTEX_SHADER, vs));
    gl.attachShader(p, shader(gl.FRAGMENT_SHADER, fs));
    gl.bindAttribLocation(p, 0, 'a');
    gl.bindAttribLocation(p, 1, 'c');
    gl.linkProgram(p);
    if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(p));
    const u = {};
    const n = gl.getProgramParameter(p, gl.ACTIVE_UNIFORMS);
    for (let i = 0; i < n; i++) {
      const info = gl.getActiveUniform(p, i);
      u[info.name.replace(/\[0\]$/, '')] = gl.getUniformLocation(p, info.name);
    }
    return { p, u };
  }

  const HEAD = '#version 300 es\nprecision highp float;\n';
  // unit quad (0..1) placed by a 3x3 matrix straight into clip space
  const QUAD_VS = HEAD + `in vec2 a; uniform mat3 u_m; out vec2 v_uv;
    void main() { v_uv = a; vec3 p = u_m * vec3(a, 1.0); gl_Position = vec4(p.xy, 0.0, 1.0); }`;
  const FULL_VS = HEAD + `in vec2 a; out vec2 v_uv; void main() { v_uv = a; gl_Position = vec4(a * 2.0 - 1.0, 0.0, 1.0); }`;
  // the pixel this fragment is in, top-left origin, the same frame as the layout (and minus the layer's offset)
  const PIXEL = `vec2 pixel(vec2 uv, vec2 res, vec2 off) { return vec2(uv.x, 1.0 - uv.y) * res - off; }
    float hsh(vec2 p) { p = fract(p * vec2(123.34, 456.21)); p += dot(p, p + 45.32); return fract(p.x * p.y); }`;

  const P = {};
  function buildPrograms() {
    // An image, optionally scrolling (wrapping around) and rippled: two samples of a tiled normal map, drifting apart,
    // push the image's coordinates the way water bends what is under it.
    P.image = program(QUAD_VS, HEAD + `in vec2 v_uv; uniform sampler2D u_tex; uniform vec4 u_uv; uniform vec4 u_tint; uniform float u_opacity;
      uniform int u_ripple; uniform sampler2D u_normal; uniform vec2 u_tiles; uniform vec4 u_drift; uniform float u_ramount; uniform vec2 u_scroll;
      uniform int u_maskOn; uniform sampler2D u_mask; uniform vec2 u_mscroll;
      uniform int u_tmOn; uniform sampler2D u_tmask; uniform vec2 u_tmscroll; uniform vec3 u_tmcolor;
      uniform int u_lightOn; uniform vec3 u_lpos; uniform vec3 u_light; uniform float u_aspect;
      out vec4 o;
      void main() {
        vec2 base = u_uv.xy + v_uv * u_uv.zw, uv = base;
        if (u_ripple == 1) {
          vec3 n1 = texture(u_normal, (uv + u_drift.xy) * u_tiles).xyz * 2.0 - 1.0;
          vec3 n2 = texture(u_normal, (uv * 1.333 + u_drift.zw) * u_tiles).xyz * 2.0 - 1.0;
          uv += normalize(vec3(n1.xy + n2.xy, n1.z)).xy * u_ramount;
        }
        vec4 c = texture(u_tex, uv + u_scroll);
        // masks slide on their own over the layer: one paints a colour in (premultiplied), one takes opacity away
        if (u_tmOn == 1) c.rgb = mix(c.rgb, u_tmcolor * c.a, texture(u_tmask, base + u_tmscroll).r);
        if (u_maskOn == 1) c *= texture(u_mask, base + u_mscroll).r;
        if (u_lightOn == 1) {
          // a point light above the flat image, in image heights: ambient plus intensity * cos(angle) * (radius / distance)^2
          vec3 d = vec3((base - u_lpos.xy) * vec2(u_aspect, 1.0), -u_lpos.z);
          float dist = length(d);
          c.rgb *= u_light.x + u_light.y * (u_lpos.z / dist) * pow(u_light.z / dist, 2.0);
        }
        o = vec4(c.rgb * u_tint.rgb, c.a) * u_opacity * u_tint.a;
      }`);

    P.gradient = program(FULL_VS, HEAD + `in vec2 v_uv; uniform vec2 u_res; uniform vec2 u_offset; uniform int u_kind;
      uniform vec2 u_dir; uniform vec2 u_range; uniform vec2 u_center; uniform float u_radius; uniform float u_opacity;
      uniform vec4 u_col[8]; uniform float u_pos[8]; uniform int u_n; out vec4 o;
      void main() {
        vec2 p = vec2(v_uv.x, 1.0 - v_uv.y) * u_res - u_offset;
        float t = u_kind == 0 ? (dot(p, u_dir) - u_range.x) / max(1.0, u_range.y - u_range.x) : distance(p, u_center) / max(1.0, u_radius);
        t = clamp(t, 0.0, 1.0);
        vec4 c = u_col[0];
        for (int i = 1; i < 8; i++) {
          if (i >= u_n) break;
          float a = u_pos[i - 1], b = u_pos[i];
          if (t >= a) c = mix(u_col[i - 1], u_col[i], clamp((t - a) / max(1e-5, b - a), 0.0, 1.0));
        }
        o = vec4(c.rgb * c.a, c.a) * u_opacity;
      }`);

    P.shape = program(QUAD_VS, HEAD + `in vec2 v_uv; uniform vec2 u_box; uniform int u_shape; uniform vec2 u_half;
      uniform float u_thick; uniform float u_soft; uniform vec4 u_color; uniform float u_opacity; out vec4 o;
      void main() {
        vec2 q = (v_uv - 0.5) * u_box;
        float d;
        if (u_shape == 0) d = length(q) - u_half.x;
        else if (u_shape == 1) d = abs(length(q) - u_half.x) - u_thick * 0.5;
        else { vec2 e = abs(q) - u_half; d = length(max(e, 0.0)) + min(max(e.x, e.y), 0.0); }
        float a = d <= 0.0 ? 1.0 : (u_soft > 1.0 ? pow(clamp(1.0 - d / u_soft, 0.0, 1.0), 2.2) : clamp(1.0 - d, 0.0, 1.0));
        a *= u_color.a * u_opacity;
        o = vec4(u_color.rgb * a, a);
      }`);

    // Triangles with a colour per vertex, in pixels: polygons, the cube, the tendrils.
    P.flat = program(HEAD + `in vec2 a; in vec4 c; uniform vec2 u_res; out vec4 v_c;
      void main() { v_c = c; gl_Position = vec4(a.x / u_res.x * 2.0 - 1.0, 1.0 - a.y / u_res.y * 2.0, 0.0, 1.0); }`,
      HEAD + `in vec4 v_c; uniform float u_opacity; out vec4 o; void main() { float a = v_c.a * u_opacity; o = vec4(v_c.rgb * a, a); }`);

    // Particles: every position is a closed-form function of the loop phase and the particle's seeded randoms,
    // with whole numbers of wraps and sways per loop, so the last frame of the loop equals the first.
    P.particles = program(HEAD + `in vec2 a; uniform vec2 u_res; uniform float u_s; uniform float u_ph; uniform float u_L;
      uniform int u_preset; uniform float u_seed; uniform float u_size; uniform float u_speed; uniform float u_angle;
      uniform float u_len; uniform vec4 u_cols[6]; uniform int u_ncols; uniform vec4 u_color;
      uniform int u_colorX; uniform float u_shift; uniform float u_tips; uniform float u_spread; uniform vec2 u_thick; uniform float u_lspread;
      uniform vec2 u_offset; out vec2 v_q; out float v_alpha; flat out int v_preset; flat out vec4 v_col; flat out float v_tip;
      flat out vec2 v_size;
      float h(float n) { return fract(sin(n * 12.9898 + u_seed * 78.233) * 43758.5453); }
      float wraps(float seconds) { return max(1.0, floor(u_L / seconds + 0.5)); }
      void main() {
        float id = float(gl_InstanceID);
        float r1 = h(id * 7.1 + 1.0), r2 = h(id * 7.1 + 2.0), r3 = h(id * 7.1 + 3.0), r4 = h(id * 7.1 + 4.0), r5 = h(id * 7.1 + 5.0), r6 = h(id * 7.1 + 6.0);
        vec2 c; vec2 size; float alpha; float rot = 0.0;
        v_tip = 0.0;
        v_col = u_ncols > 0 ? u_cols[int(min(float(u_ncols) - 1.0, floor(r5 * float(u_ncols))))] : u_color;
        if (u_preset == 0) {            // rain: thin streaks falling a whole number of times per loop
          float k = wraps(0.9 / (u_speed * (0.7 + 0.6 * r3)));
          float y = fract(r1 + k * u_ph);
          float slant = tan(radians(u_angle));
          c = vec2((r2 - 0.1 * slant) * u_res.x + slant * (y * u_res.y), y * (u_res.y * 1.1) - 0.05 * u_res.y);
          size = vec2(u_size * u_s * (0.6 + 0.5 * r4), u_s * (0.035 + 0.035 * r5));
          rot = -radians(u_angle);
          alpha = 0.18 + 0.32 * r6;
        } else if (u_preset == 1) {     // snow: slow fall with a sway
          float k = wraps(9.0 / (u_speed * (0.6 + 0.6 * r3)));
          float m = wraps(3.5 + 4.0 * r5);
          float y = fract(r1 + k * u_ph);
          c = vec2(r2 * u_res.x + 0.025 * u_s * sin(6.2831853 * (m * u_ph + r4)), y * (u_res.y * 1.08) - 0.04 * u_res.y);
          float sz = u_size * u_s * (0.5 + 0.9 * r6);
          size = vec2(sz);
          alpha = 0.45 + 0.45 * r6;
        } else if (u_preset == 2) {     // dust: drifting motes that come and go
          float m1 = wraps(7.0 + 6.0 * r3), m2 = wraps(9.0 + 6.0 * r4), m3 = wraps(3.0 + 3.0 * r5);
          c = vec2(r2 * u_res.x + 0.04 * u_s * sin(6.2831853 * (m1 * u_ph + r3)), r1 * u_res.y + 0.04 * u_s * cos(6.2831853 * (m2 * u_ph + r4)));
          size = vec2(u_size * u_s * (0.4 + 0.8 * r6));
          alpha = 0.15 + 0.5 * pow(0.5 + 0.5 * sin(6.2831853 * (m3 * u_ph + r5)), 2.0);
        } else if (u_preset == 3) {     // fireflies: wander in the lower part and blink
          float m1 = wraps(6.0 + 5.0 * r3), m2 = wraps(8.0 + 5.0 * r4), m3 = wraps(2.0 + 3.0 * r5), m4 = wraps(3.0 + 2.0 * r6);
          c = vec2(r2 * u_res.x + 0.06 * u_s * sin(6.2831853 * (m1 * u_ph + r3)) + 0.02 * u_s * sin(6.2831853 * (m4 * u_ph + r6)),
                   (0.35 + 0.6 * r1) * u_res.y + 0.05 * u_s * cos(6.2831853 * (m2 * u_ph + r4)));
          size = vec2(u_size * u_s * (0.7 + 0.6 * r6)) * 6.0;
          alpha = pow(max(0.0, sin(6.2831853 * (m3 * u_ph + r5))), 3.0);
        } else {                        // streaks: long lines crossing the screen along the angle, head bright
          float a0 = radians(u_angle);
          vec2 dir = vec2(cos(a0), sin(a0)), perp = vec2(-dir.y, dir.x);
          float len = u_len * u_s * (1.0 - 0.5 * u_lspread + u_lspread * r4);
          float across = abs(u_res.x * perp.x) + abs(u_res.y * perp.y);
          float along = abs(u_res.x * dir.x) + abs(u_res.y * dir.y) + 2.0 * len;
          float k = wraps(along / (u_speed * u_s * (1.0 - u_spread + 2.0 * u_spread * r3)));
          c = u_res * 0.5 + perp * (r2 - 0.5) * across + dir * (fract(r1 + k * u_ph) - 0.5) * along;
          size = vec2(len, u_size * u_s * mix(u_thick.x, u_thick.y, r6));
          rot = a0;
          alpha = 0.35 + 0.65 * r6;
          // colour by where the streak is across the screen: the palette as bands that wrap around and slide
          if (u_colorX == 1 && u_ncols > 0) {
            float n = float(u_ncols), x = fract(c.x / u_res.x + u_shift) * n;
            int i0 = int(min(n - 1.0, floor(x)));
            int i1 = int(mod(float(i0) + 1.0, n));
            v_col = mix(u_cols[i0], u_cols[i1], smoothstep(0.0, 1.0, fract(x)));
          }
          // a few have a white-hot head, the rest stay dim
          if (u_tips > 0.0) {
            v_tip = h(id * 7.1 + 8.0) < u_tips ? 1.0 : 0.0;
            alpha = v_tip > 0.5 ? 0.9 : 0.1 + 0.3 * r6;
          }
        }
        vec2 local = (a - 0.5) * size;
        local = vec2(cos(rot) * local.x - sin(rot) * local.y, sin(rot) * local.x + cos(rot) * local.y);
        vec2 p = c + local + u_offset;
        v_q = a - 0.5; v_alpha = alpha; v_preset = u_preset; v_size = size;
        gl_Position = vec4(p.x / u_res.x * 2.0 - 1.0, 1.0 - p.y / u_res.y * 2.0, 0.0, 1.0);
      }`, HEAD + `in vec2 v_q; in float v_alpha; flat in int v_preset; flat in vec4 v_col; flat in float v_tip; flat in vec2 v_size;
      uniform float u_opacity; uniform int u_pill; uniform float u_tipLen; uniform float u_tailPow; uniform int u_flat; out vec4 o;
      void main() {
        float a;
        vec3 rgb = v_col.rgb;
        if (v_tip > 0.5) rgb = mix(rgb, vec3(1.0), smoothstep(0.5 - u_tipLen, 0.5, v_q.x));
        if (v_preset == 0) a = exp(-pow(v_q.x * 4.0, 2.0)) * smoothstep(0.5, 0.0, abs(v_q.y));
        else if (v_preset == 3) { float r = length(v_q) * 2.0; a = exp(-r * r * 18.0) + 0.35 * exp(-r * r * 3.5); }
        else if (v_preset == 4 && u_pill == 1) {
          // an even bar with rounded ends, in pixels
          vec2 q = v_q * v_size;
          float r = v_size.y * 0.5;
          float d = length(vec2(max(abs(q.x) - (v_size.x * 0.5 - r), 0.0), q.y)) - r;
          a = clamp(0.5 - d, 0.0, 1.0);
        }
        else if (v_preset == 4) a = pow(clamp(v_q.x + 0.5, 0.0, 1.0), u_tailPow)
          * (u_flat == 1 ? clamp((0.5 - abs(v_q.y)) * v_size.y + 0.5, 0.0, 1.0) : exp(-pow(v_q.y * 3.2, 2.0)));
        else { float r = length(v_q) * 2.0; a = smoothstep(1.0, 0.25, r); }
        a *= (u_pill == 1 && v_preset == 4 ? 1.0 : v_alpha) * v_col.a * u_opacity;   // pills keep their full colour
        o = vec4(rgb * a, a);
      }`);

    // Columns of glyphs falling: a head and a fading trail per column, the glyphs changing a whole number of times per loop.
    P.code = program(FULL_VS, HEAD + PIXEL + `in vec2 v_uv; uniform vec2 u_res; uniform vec2 u_offset; uniform float u_cell; uniform float u_ph;
      uniform float u_L; uniform float u_seed; uniform float u_speed; uniform float u_trail; uniform float u_density; uniform float u_change;
      uniform vec4 u_color; uniform vec4 u_head; uniform sampler2D u_atlas; uniform float u_glyphs; uniform float u_opacity; out vec4 o;
      float h(float n) { return fract(sin(n * 12.9898 + u_seed * 78.233) * 43758.5453); }
      void main() {
        vec2 p = pixel(v_uv, u_res, u_offset);
        vec2 cell = floor(p / u_cell), f = fract(p / u_cell);
        float col = cell.x;
        o = vec4(0.0);
        for (int stream = 0; stream < 2; stream++) {
          float sid = col * 3.17 + float(stream) * 91.3;
          if (h(sid + 3.0) > u_density) continue;
          float rows = u_res.y / u_cell, span = rows + u_trail;
          float k = max(1.0, floor(u_L * u_speed * (0.45 + h(sid + 1.0)) * 12.0 / span + 0.5));
          float head = fract(h(sid + 2.0) + k * u_ph) * span;
          float behind = head - cell.y;
          if (behind < 0.0 || behind > u_trail) continue;
          float bright = pow(1.0 - behind / u_trail, 1.4);
          float change = mod(floor(u_ph * u_change + h(cell.x * 7.0 + cell.y * 13.0) * u_change), u_change);
          float g = floor(hsh(cell + vec2(change * 0.731, u_seed)) * u_glyphs);
          vec2 at = (vec2(mod(g, 16.0), floor(g / 16.0)) + vec2(f.x, f.y)) / 16.0;
          float ink = texture(u_atlas, at).a;
          vec4 c = behind < 1.0 ? u_head : u_color;
          float a = ink * bright * c.a * u_opacity;
          o = max(o, vec4(c.rgb * a, a));
        }
      }`);

    // A band filled under a moving curve (a sum of three waves), turned by an angle, with a soft shadow above its edge.
    P.wave = program(FULL_VS, HEAD + PIXEL + `in vec2 v_uv; uniform vec2 u_res; uniform vec2 u_offset; uniform vec2 u_center; uniform float u_angle;
      uniform float u_base; uniform vec3 u_amp; uniform vec3 u_wl; uniform vec3 u_k; uniform vec3 u_phase; uniform float u_ph;
      uniform vec4 u_color; uniform vec4 u_color2; uniform float u_grad; uniform float u_shadow; uniform float u_shadowW; uniform float u_opacity; out vec4 o;
      void main() {
        vec2 p = pixel(v_uv, u_res, u_offset) - u_center;
        float ca = cos(u_angle), sa = sin(u_angle);
        vec2 q = vec2(ca * p.x + sa * p.y, -sa * p.x + ca * p.y);
        float edge = u_base;
        for (int i = 0; i < 3; i++) edge += u_amp[i] * sin(6.2831853 * (q.x / u_wl[i] - u_k[i] * u_ph) + u_phase[i]);
        float d = q.y - edge;
        float a = clamp(d + 0.5, 0.0, 1.0);
        vec4 c = mix(u_color, u_color2, clamp(d / max(1.0, u_grad), 0.0, 1.0));
        // the shadow falls off like a bell above the edge, the way a cut-paper layer's does
        float sh = d < 0.0 ? u_shadow * exp(-pow(d / max(1.0, u_shadowW), 2.0)) : 0.0;
        float fill = a * c.a;
        o = vec4(c.rgb * fill, fill + sh * (1.0 - fill)) * u_opacity;
      }`);

    // Smoke, sand, rock: layered value noise lit as a relief. It drifts on circles a whole number of times per loop.
    P.noise = program(FULL_VS, HEAD + PIXEL + `in vec2 v_uv; uniform vec2 u_res; uniform vec2 u_offset; uniform float u_scale; uniform float u_ph;
      uniform float u_drift; uniform int u_oct; uniform int u_ridged; uniform float u_seed; uniform float u_relief; uniform vec2 u_light;
      uniform vec4 u_col[4]; uniform int u_ncol; uniform float u_contrast; uniform float u_shine; uniform float u_opacity; out vec4 o;
      float vn(vec2 p) {
        vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
        float a = hsh(i + u_seed), b = hsh(i + vec2(1.0, 0.0) + u_seed), c = hsh(i + vec2(0.0, 1.0) + u_seed), d = hsh(i + vec2(1.0) + u_seed);
        return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
      }
      float fbm(vec2 p) {
        float s = 0.0, amp = 0.5, norm = 0.0;
        for (int i = 0; i < 7; i++) {
          if (i >= u_oct) break;
          float k = float(i % 2 + 1), ph = 6.2831853 * (k * u_ph + float(i) * 0.37);
          vec2 q = p + u_drift * vec2(cos(ph), sin(ph)) / (1.0 + float(i) * 0.5);
          float v = vn(q);
          if (u_ridged == 1) v = 1.0 - abs(v * 2.0 - 1.0);
          s += v * amp; norm += amp; amp *= 0.5; p = mat2(1.6, 1.2, -1.2, 1.6) * p;
        }
        return s / norm;
      }
      void main() {
        vec2 p = pixel(v_uv, u_res, u_offset) / u_scale;
        float e = 1.5 / u_scale;
        float hgt = fbm(p);
        // the slope from the neighbouring pixels (one noise evaluation instead of three: 54 -> 60 fps at 3440x1440);
        // y is flipped because the layout runs top to bottom and the screen bottom to top
        float lit = 1.0;
        if (u_relief > 0.0) {
          vec3 n = normalize(vec3(1.5 * dFdx(hgt) * -u_relief, 1.5 * dFdy(hgt) * u_relief, e * 2.0));
          lit = clamp(dot(n, normalize(vec3(u_light, 0.7))), 0.0, 1.0);
        }
        float t = clamp((hgt - 0.5) * u_contrast + 0.5, 0.0, 1.0) * float(u_ncol - 1);
        int i0 = int(min(floor(t), float(u_ncol - 2)));
        vec4 c = mix(u_col[i0], u_col[i0 + 1], t - float(i0));
        vec3 rgb = c.rgb * (0.35 + 0.75 * lit) + u_shine * pow(lit, 24.0);
        o = vec4(rgb * c.a, c.a) * u_opacity;
      }`);

    // A sea seen at a grazing angle: sharp-crested waves, each one dragging the smaller ones (that bunches them into
    // choppy crests), lit by one light, with an optional grain of sand riding on the surface. Every wave turns a whole
    // number of times per loop.
    P.water = program(FULL_VS, HEAD + PIXEL + `in vec2 v_uv; uniform vec2 u_res; uniform vec2 u_offset; uniform float u_scale; uniform float u_ph;
      uniform float u_horizon; uniform float u_angle; uniform float u_spread; uniform float u_height; uniform float u_choppy;
      uniform float u_speed; uniform int u_iter; uniform float u_seed; uniform vec3 u_light; uniform vec4 u_deep; uniform vec4 u_crest;
      uniform vec4 u_sky; uniform float u_shine; uniform float u_gloss; uniform float u_sand; uniform float u_sandSize; uniform float u_fade;
      uniform float u_opacity; out vec4 o;
      void main() {
        vec2 p = pixel(v_uv, u_res, u_offset);
        float yn = clamp(p.y / u_res.y, -0.2, 1.6);
        float z = 1.0 / (yn + u_horizon);
        vec2 g = vec2((p.x - 0.5 * u_res.x) / u_res.y * z, z) * (1.0 + u_horizon) * u_res.y / u_scale;
        float H = 0.0, A = 1.0, f = 1.0, sumA = 0.0;
        vec2 dH = vec2(0.0);
        for (int i = 0; i < 24; i++) {
          if (i >= u_iter) break;
          float a = u_angle + (fract(float(i) * 0.618034 + u_seed) - 0.5) * 2.0 * u_spread;
          vec2 d = vec2(cos(a), sin(a));
          float cyc = max(1.0, floor(u_speed * sqrt(f) + 0.5));
          float x = dot(d, g) * f - 6.2831853 * cyc * u_ph + float(i) * 1.7;
          float w = exp(sin(x) - 1.0), dw = w * cos(x);
          H += A * w; sumA += A;
          dH += A * dw * f * d;
          g -= d * dw * A * 0.38 * u_choppy;
          f *= 1.18; A *= 0.82;
        }
        H /= sumA; dH /= sumA;
        vec3 n = normalize(vec3(-dH.x * u_height, 1.0, -dH.y * u_height));
        vec3 view = normalize(vec3(0.0, mix(0.2, 0.75, clamp(yn, 0.0, 1.0)), -1.0));
        float diff = max(dot(n, u_light), 0.0);
        float spec = pow(max(dot(n, normalize(u_light + view)), 0.0), u_gloss);
        float fres = 0.02 + 0.98 * pow(1.0 - max(dot(n, view), 0.0), 5.0);
        vec3 rgb = mix(u_deep.rgb, u_crest.rgb, clamp(H * 1.3, 0.0, 1.0)) * (0.35 + 0.65 * diff) + u_sky.rgb * fres + u_shine * spec;
        if (u_sand > 0.0) {
          // grains stuck on the moving surface: brighter where the light hits, a few glinting
          float s = hsh(floor(g * u_scale / u_sandSize));
          rgb *= 1.0 + u_sand * (s - 0.5) * 2.0;
          rgb += u_sand * 0.6 * step(0.985, s) * (0.3 + diff);
        }
        // far away the waves get too small to see: fade into the deep colour (and no shimmer at the top)
        rgb = mix(rgb, u_deep.rgb, clamp((z * (1.0 + u_horizon) - 1.0) * u_fade, 0.0, 1.0));
        o = vec4(rgb, 1.0) * u_opacity;
      }`);

    // Bars of the sound spectrum, in a line or around a circle, rounded, with a colour gradient across them.
    P.bars = program(HEAD + `in vec2 a; uniform vec2 u_res; uniform float u_spec[64]; uniform float u_n; uniform int u_mode;
      uniform vec2 u_origin; uniform float u_span; uniform float u_barW; uniform float u_maxH; uniform float u_minH; uniform float u_radius;
      uniform float u_from; uniform float u_to; uniform int u_mirror; uniform float u_rot; out vec2 v_q; out vec2 v_size; out float v_t;
      void main() {
        float i = float(gl_InstanceID), t = (i + 0.5) / u_n;
        float b = mix(u_from, u_to, t);
        int b0 = int(floor(b)); float bf = fract(b);
        float level = mix(u_spec[clamp(b0, 0, 63)], u_spec[clamp(b0 + 1, 0, 63)], bf);
        float hgt = u_minH + clamp(level, 0.0, 1.5) * u_maxH;
        vec2 size, c; float rot;
        if (u_mode == 0) {
          size = vec2(u_barW, u_mirror == 1 ? hgt * 2.0 : hgt);
          c = vec2(u_origin.x - u_span * 0.5 + t * u_span, u_mirror == 1 ? u_origin.y : u_origin.y - hgt * 0.5);
          rot = 0.0;
        } else {
          float th = u_rot + 6.2831853 * t;
          vec2 dir = vec2(sin(th), -cos(th));
          size = vec2(u_barW, hgt);
          c = u_origin + dir * (u_radius + hgt * 0.5);
          rot = th;
        }
        vec2 local = (a - 0.5) * size;
        local = vec2(cos(rot) * local.x - sin(rot) * local.y, sin(rot) * local.x + cos(rot) * local.y);
        vec2 p = c + local;
        v_q = (a - 0.5) * size; v_size = size; v_t = t;
        gl_Position = vec4(p.x / u_res.x * 2.0 - 1.0, 1.0 - p.y / u_res.y * 2.0, 0.0, 1.0);
      }`, HEAD + `in vec2 v_q; in vec2 v_size; in float v_t; uniform vec4 u_col0; uniform vec4 u_col1; uniform float u_opacity; out vec4 o;
      void main() {
        float r = min(v_size.x, v_size.y) * 0.5;
        vec2 e = abs(v_q) - (v_size * 0.5 - r);
        float d = length(max(e, 0.0)) + min(max(e.x, e.y), 0.0) - r;
        float a = clamp(0.5 - d, 0.0, 1.0);
        vec4 c = mix(u_col0, u_col1, v_t);
        a *= c.a * u_opacity;
        o = vec4(c.rgb * a, a);
      }`);

    // ---------------------------------------------------------------- post effects (on a full screen quad)
    P.copy = program(FULL_VS, HEAD + `in vec2 v_uv; uniform sampler2D u_tex; out vec4 o; void main() { o = texture(u_tex, v_uv); }`);
    P.blur = program(FULL_VS, HEAD + `in vec2 v_uv; uniform sampler2D u_tex; uniform vec2 u_step; out vec4 o;
      void main() {
        vec4 s = vec4(0.0); float w = 0.0;
        for (int i = -7; i <= 7; i++) { float g = exp(-float(i * i) / 18.0); s += texture(u_tex, v_uv + u_step * float(i)) * g; w += g; }
        o = s / w;
      }`);
    P.grade = program(FULL_VS, HEAD + `in vec2 v_uv; uniform sampler2D u_tex; uniform float u_bright; uniform float u_contrast;
      uniform float u_sat; uniform float u_hue; uniform vec4 u_tint; uniform float u_tintAmount; out vec4 o;
      void main() {
        vec3 c = texture(u_tex, v_uv).rgb;
        c += u_bright;
        c = (c - 0.5) * u_contrast + 0.5;
        float l = dot(c, vec3(0.299, 0.587, 0.114));
        c = mix(vec3(l), c, u_sat);
        // hue rotation in YIQ
        vec3 yiq = mat3(0.299, 0.596, 0.211, 0.587, -0.274, -0.523, 0.114, -0.322, 0.312) * c;
        float hcos = cos(u_hue), hsin = sin(u_hue);
        yiq = vec3(yiq.x, yiq.y * hcos - yiq.z * hsin, yiq.y * hsin + yiq.z * hcos);
        c = mat3(1.0, 1.0, 1.0, 0.956, -0.272, -1.106, 0.621, -0.647, 1.703) * yiq;
        c = mix(c, c * u_tint.rgb, u_tintAmount);
        o = vec4(clamp(c, 0.0, 1.0), 1.0);
      }`);
    P.grain = program(FULL_VS, HEAD + `in vec2 v_uv; uniform sampler2D u_tex; uniform float u_amount; uniform float u_frame; uniform vec2 u_res;
      uniform int u_mult; out vec4 o;
      // an integer hash: the sine one showed stripes when the grain was strong
      float h(vec2 p) {
        uvec2 q = uvec2(ivec2(p) + 65536) * uvec2(1597334673u, 3812015801u);
        uint n = (q.x ^ q.y) + uint(u_frame) * 747796405u;
        n ^= n >> 16; n *= 2246822519u; n ^= n >> 13; n *= 3266489917u; n ^= n >> 16;
        return float(n) / 4294967295.0;
      }
      void main() {
        vec3 c = texture(u_tex, v_uv).rgb; float n = h(floor(v_uv * u_res)) - 0.5;
        // "multiply": the grain scales with the brightness, so black stays black
        o = vec4(clamp(u_mult == 1 ? c * (1.0 + 2.0 * n * u_amount) : c + n * u_amount, 0.0, 1.0), 1.0);
      }`);
    P.threshold = program(FULL_VS, HEAD + `in vec2 v_uv; uniform sampler2D u_tex; uniform float u_threshold; out vec4 o;
      void main() { vec3 c = texture(u_tex, v_uv).rgb; float l = max(c.r, max(c.g, c.b)); o = vec4(c * smoothstep(u_threshold, u_threshold + 0.15, l), 1.0); }`);
    P.add = program(FULL_VS, HEAD + `in vec2 v_uv; uniform sampler2D u_tex; uniform sampler2D u_add; uniform float u_strength; out vec4 o;
      void main() { o = vec4(clamp(texture(u_tex, v_uv).rgb + texture(u_add, v_uv).rgb * u_strength, 0.0, 1.0), 1.0); }`);
    P.rays = program(FULL_VS, HEAD + `in vec2 v_uv; uniform sampler2D u_tex; uniform vec2 u_light; uniform float u_length; out vec4 o;
      void main() {
        vec2 d = (u_light - v_uv) * u_length / 48.0; vec2 p = v_uv; vec3 s = vec3(0.0); float w = 1.0, tw = 0.0;
        for (int i = 0; i < 48; i++) { s += texture(u_tex, p).rgb * w; tw += w; w *= 0.965; p += d; }
        o = vec4(s / tw, 1.0);
      }`);
    // the whole picture moved and slightly enlarged (so no edge shows): the camera shaking with the bass
    P.shift = program(FULL_VS, HEAD + `in vec2 v_uv; uniform sampler2D u_tex; uniform vec2 u_off; uniform float u_zoom; out vec4 o;
      void main() { o = texture(u_tex, (v_uv - 0.5) / u_zoom + 0.5 + u_off); }`);
    // red and blue pulled apart sideways
    P.chroma = program(FULL_VS, HEAD + `in vec2 v_uv; uniform sampler2D u_tex; uniform float u_amount; out vec4 o;
      void main() {
        vec2 d = (v_uv - 0.5) * u_amount;
        o = vec4(texture(u_tex, v_uv + d).r, texture(u_tex, v_uv).g, texture(u_tex, v_uv - d).b, 1.0);
      }`);

    // Water over the whole frame: the same two drifting samples of a tiled normal map as the image ripple.
    P.ripple = program(FULL_VS, HEAD + `in vec2 v_uv; uniform sampler2D u_tex; uniform sampler2D u_normal; uniform vec2 u_tiles;
      uniform vec4 u_drift; uniform vec2 u_amount; out vec4 o;
      void main() {
        vec3 n1 = texture(u_normal, (v_uv + u_drift.xy) * u_tiles).xyz * 2.0 - 1.0;
        vec3 n2 = texture(u_normal, (v_uv * 1.333 + u_drift.zw) * u_tiles).xyz * 2.0 - 1.0;
        o = texture(u_tex, v_uv + normalize(vec3(n1.xy + n2.xy, n1.z)).xy * u_amount);
      }`);

    // Barrel (amount < 0) or pincushion lens, worked out in a frame of its own aspect that covers the screen
    // (u_fscale: how much of that frame the screen shows), so a lens made for 16:9 bends the same on an ultrawide.
    P.lens = program(FULL_VS, HEAD + `in vec2 v_uv; uniform sampler2D u_tex; uniform float u_amount; uniform vec2 u_fscale;
      uniform float u_faspect; out vec4 o;
      void main() {
        vec2 k = vec2(u_faspect, 1.0) * 2.0;
        vec2 c = (v_uv - 0.5) * u_fscale * k;
        vec2 f = c * (1.0 + u_amount * dot(c, c)) / k;
        o = texture(u_tex, 0.5 + f / u_fscale);
      }`);
  }

  // ------------------------------------------------------------------ geometry and render targets
  const quad = gl.createBuffer();
  gl.bindBuffer(gl.ARRAY_BUFFER, quad);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([0, 0, 1, 0, 0, 1, 0, 1, 1, 0, 1, 1]), gl.STATIC_DRAW);
  const vao = gl.createVertexArray();
  gl.bindVertexArray(vao);
  gl.enableVertexAttribArray(0);
  gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);
  // triangles built on the CPU each frame: x, y, r, g, b, a per vertex
  const flatBuf = gl.createBuffer();
  const flatVao = gl.createVertexArray();
  gl.bindVertexArray(flatVao);
  gl.bindBuffer(gl.ARRAY_BUFFER, flatBuf);
  gl.enableVertexAttribArray(0);
  gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 24, 0);
  gl.enableVertexAttribArray(1);
  gl.vertexAttribPointer(1, 4, gl.FLOAT, false, 24, 8);
  gl.bindVertexArray(vao);

  function target(w, h) {
    const tex = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, tex);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, w, h, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    const fb = gl.createFramebuffer();
    gl.bindFramebuffer(gl.FRAMEBUFFER, fb);
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, tex, 0);
    return { tex, fb, w, h };
  }
  let T = null;   // { a, b, c, d, w, h }
  function ensureTargets(w, h) {
    if (T && T.w === w && T.h === h) return;
    if (T) for (const k of ['a', 'b', 'c', 'd']) { gl.deleteTexture(T[k].tex); gl.deleteFramebuffer(T[k].fb); }
    const hw = Math.max(1, w >> 1), hh = Math.max(1, h >> 1);
    T = { a: target(w, h), b: target(w, h), c: target(hw, hh), d: target(hw, hh), w, h };
  }
  function bindTarget(t) { gl.bindFramebuffer(gl.FRAMEBUFFER, t ? t.fb : null); gl.viewport(0, 0, t ? t.w : canvas.width, t ? t.h : canvas.height); }
  function full(prog, setup, out) {
    bindTarget(out);
    gl.disable(gl.BLEND);
    gl.useProgram(prog.p);
    setup(prog.u);
    gl.drawArrays(gl.TRIANGLES, 0, 6);
  }
  // a full screen layer drawn onto the current target with the layer's blend (unlike `full`, which replaces)
  function layerPass(prog, setup) {
    gl.useProgram(prog.p);
    setup(prog.u);
    gl.drawArrays(gl.TRIANGLES, 0, 6);
  }
  function tex(unit, t) { gl.activeTexture(gl.TEXTURE0 + unit); gl.bindTexture(gl.TEXTURE_2D, t.tex); }

  // matrix mapping the unit quad to a pixel rectangle (centre, size, rotation) and then to clip space
  function rectMatrix(cx, cy, w, h, rotDeg, W, H) {
    const r = rotDeg * Math.PI / 180, c = Math.cos(r), s = Math.sin(r);
    // p = C + R * ((a - 0.5) * size); clip = (p / res) * 2 - 1, y flipped
    const ax = c * w, bx = -s * h, ay = s * w, by = c * h;
    const tx = cx - 0.5 * ax - 0.5 * bx, ty = cy - 0.5 * ay - 0.5 * by;
    const sx = 2 / W, sy = -2 / H;
    return new Float32Array([ax * sx, ay * sy, 0, bx * sx, by * sy, 0, tx * sx - 1, ty * sy + 1, 1]);
  }

  function setBlend(mode) {
    gl.enable(gl.BLEND);
    gl.blendEquation(gl.FUNC_ADD);
    if (mode === 'add') gl.blendFunc(gl.ONE, gl.ONE);
    else if (mode === 'screen') gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_COLOR);
    else if (mode === 'multiply') gl.blendFunc(gl.DST_COLOR, gl.ONE_MINUS_SRC_ALPHA);
    else gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
  }

  function drawFlat(verts, count, CW, CH, opacity) {
    if (count === 0) return;
    gl.bindVertexArray(flatVao);
    gl.bindBuffer(gl.ARRAY_BUFFER, flatBuf);
    gl.bufferData(gl.ARRAY_BUFFER, verts.subarray(0, count * 6), gl.DYNAMIC_DRAW);
    gl.useProgram(P.flat.p);
    gl.uniform2f(P.flat.u.u_res, CW, CH);
    gl.uniform1f(P.flat.u.u_opacity, opacity);
    gl.drawArrays(gl.TRIANGLES, 0, count);
    gl.bindVertexArray(vao);
  }

  // ------------------------------------------------------------------ the wallpaper
  let W = null;        // parsed wallpaper
  let LOOP = 20;
  const images = new Map();
  // the sound: 64 bands (left and right averaged, low to high) and four summaries
  const spectrum = new Float32Array(64);
  const audio = { bass: 0, mid: 0, treble: 0, all: 0 };
  let realtime = false;
  const ANCHORS = {
    'center': [0.5, 0.5], 'top': [0.5, 0], 'bottom': [0.5, 1], 'left': [0, 0.5], 'right': [1, 0.5],
    'top-left': [0, 0], 'top-right': [1, 0], 'bottom-left': [0, 1], 'bottom-right': [1, 1],
  };
  const PARTICLES = { rain: [0, 900], snow: [1, 380], dust: [2, 160], fireflies: [3, 60], streaks: [4, 120] };

  /** A band by name (bass, mid, treble, all), by number (0 to 63) or as [from, to]. */
  function level(band) {
    if (typeof band === 'number') return spectrum[clamp(Math.round(band), 0, 63)];
    if (Array.isArray(band) && band.length === 2) {
      const a = clamp(Math.round(band[0]), 0, 63), b = clamp(Math.round(band[1]), a, 63);
      let s = 0;
      for (let i = a; i <= b; i++) s += spectrum[i];
      return s / (b - a + 1);
    }
    return audio[band] ?? audio.all;
  }
  function summarize() {
    const avg = (from, to) => { let s = 0; for (let i = from; i < to; i++) s += spectrum[i]; return s / (to - from); };
    audio.bass = avg(0, 8); audio.mid = avg(8, 32); audio.treble = avg(32, 64); audio.all = avg(0, 64);
  }
  // A plausible spectrum moving with t (strong lows, a beat about twice a second), for stills and tests only.
  function fakeAudio(t) {
    const beat = Math.pow(0.5 + 0.5 * Math.cos(TAU * t * 2), 6);
    for (let i = 0; i < 64; i++) {
      const fall = Math.pow(1 - i / 72, 1.6);
      spectrum[i] = clamp(fall * (0.35 + 0.45 * beat * (i < 10 ? 1 : 0.4)) * (0.75 + 0.25 * Math.sin(t * 5.3 + i * 0.61)), 0, 1);
    }
    summarize();
  }

  function snapPeriod(p, where) {
    p = num(p, LOOP);
    const n = Math.max(1, Math.round(LOOP / p));
    const snapped = LOOP / n;
    if (Math.abs(snapped - p) > 1e-3) warn(`${where}: período ${p} não divide o loop de ${LOOP} s; usando ${snapped.toFixed(3)}`);
    return snapped;
  }
  /** Whole cycles per loop for a speed given as cycles per period in seconds (so the loop closes). */
  function cycles(period) {
    const p = num(period, LOOP);
    return Math.max(1, Math.round(LOOP / Math.max(0.05, Math.abs(p)))) * (p < 0 ? -1 : 1);
  }

  /** Everything that only makes sense live: sound, in any of the forms it can be asked for, and the mouse. */
  function wantsSound(l) {
    if (l.animate.some((a) => a.type === 'audio')) return true;
    if (l.type === 'spectrum' || l.type === 'electric') return true;
    return l.audio != null && typeof l.audio === 'object';
  }

  async function load() {
    const res = await fetch('wallpaper.json', { cache: 'no-store' });
    if (!res.ok) throw new Error('wallpaper.json não encontrado');
    W = await res.json();
    if (W.format !== 'fundo-vivo') throw new Error('não é um wallpaper do Fundo Vivo');
    if (W.version !== 1) warn(`versão ${W.version} do formato; este runtime conhece a 1`);
    // up to an hour; past two minutes the host never turns it into a video, so it always plays live
    LOOP = clamp(num(W.loop, 20), 1, 3600);
    if (LOOP > 120) realtime = true;
    W.layers = Array.isArray(W.layers) ? W.layers : [];
    W.effects = Array.isArray(W.effects) ? W.effects : [];
    W.layers.forEach((l, i) => {
      l.animate = Array.isArray(l.animate) ? l.animate : [];
      l.animate.forEach((a, j) => {
        const where = `camada ${l.name || i + 1}, animação ${j + 1}`;
        if (a.type !== 'audio') a._period = snapPeriod(a.period, where);
        if (a.type === 'spin') {
          // whole turns per loop, or the last frame would not match the first
          const perLoop = num(a.amount, 1) * LOOP / a._period, whole = Math.round(perLoop) || Math.sign(perLoop) || 1;
          if (Math.abs(whole - perLoop) > 1e-3) warn(`${where}: ${perLoop.toFixed(2)} voltas por loop; usando ${whole}`);
          a._turns = whole;
        }
        a._phases = [1, 2, 3].map((k) => hash(i, j, k));
      });
      if (wantsSound(l)) realtime = true;
      if (l.type === 'text' && num(l.reveal, 0) > 0) l._reveal = snapPeriod(l.reveal, `camada ${l.name || i + 1}, digitação`);
    });
    if (W.effects.some((e) => e.band != null)) realtime = true;
    const sources = new Set();
    for (const l of W.layers) {
      if (l.type !== 'image') continue;
      if (l.src) sources.add(l.src);
      for (const extra of [l.ripple, l.mask, l.colorMask]) if (extra && typeof extra.src === 'string') sources.add(extra.src);
    }
    for (const e of W.effects) if (typeof e.src === 'string') sources.add(e.src);
    await Promise.all([...sources].map(async (src) => {
      if (images.has(src)) return;
      const img = new Image();
      img.src = src;
      try { await img.decode(); } catch (e) { warn(`imagem ${src} não abriu`); return; }
      const t = gl.createTexture();
      gl.bindTexture(gl.TEXTURE_2D, t);
      gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, true);
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, img);
      gl.generateMipmap(gl.TEXTURE_2D);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
      images.set(src, { tex: t, w: img.naturalWidth, h: img.naturalHeight });
    }));
    // fonts shipped inside the package: a text layer names the family in `font` and the file in `fontFile`
    await Promise.all(W.layers.filter((l) => l.type === 'text' && typeof l.fontFile === 'string' && typeof l.font === 'string').map(async (l) => {
      try { const ff = new FontFace(l.font, `url("${encodeURI(l.fontFile)}")`); await ff.load(); document.fonts.add(ff); }
      catch (e) { warn(`fonte ${l.fontFile} não abriu`); }
    }));
    if (document.fonts && document.fonts.ready) await document.fonts.ready;
  }

  // Property values of a layer at loop time tl (seconds). Any numeric property of the layer can be animated
  // (amplitude of a wave, length of the tendrils...), not only x, y, scale, rotation and opacity.
  function animated(l, tl) {
    const v = { x: num(l.x, 0), y: num(l.y, 0), scale: num(l.scale, 1), rotation: num(l.rotation, 0), opacity: num(l.opacity, 1) };
    for (const a of l.animate) {
      if (typeof a.property !== 'string') continue;
      if (!(a.property in v)) {
        // a property the layer leaves at its default: only the change is known here, prop() adds the default to it
        if (typeof l[a.property] !== 'number') (v._fromDefault ??= new Set()).add(a.property);
        v[a.property] = num(l[a.property], 0);
      }
      const amount = num(a.amount, a.type === 'spin' ? 1 : 0);
      let d = 0;
      if (a.type === 'oscillate') d = amount * Math.sin(TAU * (tl / a._period + num(a.phase, 0)));
      else if (a.type === 'spin') d = 360 * (a._turns * tl / LOOP + num(a.phase, 0));
      else if (a.type === 'noise') {
        // smooth noise that repeats every period: a few harmonics with seeded phases
        const x = tl / a._period + num(a.phase, 0);
        d = amount * (Math.sin(TAU * (x + a._phases[0])) * 0.55 + Math.sin(TAU * (2 * x + a._phases[1])) * 0.3 + Math.sin(TAU * (3 * x + a._phases[2])) * 0.15);
      } else if (a.type === 'audio') d = amount * level(a.band);
      v[a.property] += d;
    }
    v.opacity = clamp(v.opacity, 0, 1);
    return v;
  }
  const prop = (v, l, name, fallback) =>
    v._fromDefault?.has(name) ? fallback + v[name] : num(v[name], num(l[name], fallback));

  // ------------------------------------------------------------------ text, drawn once by the browser into a texture
  const textCache = new Map();
  function fontFamily(f) {
    if (f === 'mono' || !f) return '"Cascadia Mono", Consolas, "Courier New", monospace';
    if (f === 'sans') return '"Segoe UI Variable Text", "Segoe UI", sans-serif';
    return f;
  }
  /** Lines as strings or as lists of [text, colour] pieces; `chars` limits how many show (the typewriter). */
  function textTexture(l, px, chars, cursorOn) {
    const lines = (Array.isArray(l.lines) ? l.lines : String(l.text ?? '').split('\n')).map((line) =>
      Array.isArray(line) ? line.map((s) => (Array.isArray(s) ? [String(s[0]), s[1]] : [String(s), null])) : [[String(line), null]]);
    const key = [l.__id, px, chars, cursorOn].join('|');
    if (textCache.has(key)) return textCache.get(key);
    for (const [k, old] of textCache) if (k.startsWith(l.__id + '|')) { gl.deleteTexture(old.tex); textCache.delete(k); }
    const c2 = document.createElement('canvas');
    const ctx = c2.getContext('2d');
    const font = `${l.weight || 400} ${px}px ${fontFamily(l.font)}`;
    ctx.font = font;
    const lh = px * num(l.lineHeight, 1.35);
    let width = 1;
    for (const line of lines) width = Math.max(width, line.reduce((w, s) => w + ctx.measureText(s[0]).width, 0));
    const pad = Math.ceil(px * 0.5);
    c2.width = Math.ceil(width + px * 1.2) + pad * 2;
    c2.height = Math.ceil(lh * lines.length) + pad * 2;
    ctx.font = font;
    ctx.textBaseline = 'top';
    const base = l.color || '#ffffff';
    let left = chars, endX = pad, endY = pad;
    lines.forEach((line, n) => {
      const total = line.reduce((w, s) => w + ctx.measureText(s[0]).width, 0);
      let x = l.align === 'center' ? (c2.width - total) / 2 : l.align === 'right' ? c2.width - pad - total : pad;
      const y = pad + n * lh;
      for (const [s, col] of line) {
        if (left <= 0) break;
        const shown = s.slice(0, Math.max(0, left));
        left -= s.length;
        ctx.fillStyle = col || base;
        ctx.fillText(shown, x, y);
        x += ctx.measureText(shown).width;
        endX = x; endY = y;
      }
      if (left > 0) left -= 1;   // the line break counts as a character
    });
    if (cursorOn) { ctx.fillStyle = l.cursorColor || base; ctx.fillRect(endX + px * 0.08, endY + px * 0.05, px * 0.55, px * 0.95); }
    const t = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, t);
    gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, true);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, c2);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    const entry = { tex: t, w: c2.width, h: c2.height, total: lines.reduce((n, line) => n + line.reduce((m, s) => m + s[0].length, 0) + 1, 0) };
    textCache.set(key, entry);
    return entry;
  }
  function totalChars(l) {
    const lines = Array.isArray(l.lines) ? l.lines : String(l.text ?? '').split('\n');
    return lines.reduce((n, line) => n + (Array.isArray(line) ? line.reduce((m, s) => m + String(Array.isArray(s) ? s[0] : s).length, 0) : String(line).length) + 1, 0);
  }

  // ------------------------------------------------------------------ glyphs for the code rain
  let atlas = null;
  function glyphAtlas(chars) {
    if (atlas && atlas.chars === chars) return atlas;
    const size = 1024, cell = size / 16;
    const c2 = document.createElement('canvas');
    c2.width = c2.height = size;
    const ctx = c2.getContext('2d');
    ctx.fillStyle = '#fff';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.font = `${Math.round(cell * 0.78)}px "MS Gothic", "Yu Gothic", "Cascadia Mono", monospace`;
    const list = Array.from(chars).slice(0, 256);
    list.forEach((ch, i) => {
      const x = (i % 16 + 0.5) * cell, y = (Math.floor(i / 16) + 0.5) * cell;
      ctx.save(); ctx.translate(x, y); ctx.scale(-1, 1); ctx.fillText(ch, 0, 0); ctx.restore();   // mirrored, like the film
    });
    const t = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, t);
    gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, true);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, c2);
    gl.generateMipmap(gl.TEXTURE_2D);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    atlas = { tex: t, chars, count: list.length };
    return atlas;
  }
  const MATRIX_GLYPHS = 'ｱｲｳｴｵｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾄﾅﾆﾇﾈﾉﾊﾋﾌﾍﾎﾏﾐﾑﾒﾓﾔﾕﾖﾗﾘﾙﾚﾛﾜﾝ0123456789Z:."=*+-<>¦|';

  // ------------------------------------------------------------------ the cube and the tendrils, as triangles
  let flatVerts = new Float32Array(6 * 6 * 4096);
  let flatCount = 0;
  function pushTri(x1, y1, x2, y2, x3, y3, c1, c2, c3) {
    if ((flatCount + 3) * 6 > flatVerts.length) { const bigger = new Float32Array(flatVerts.length * 2); bigger.set(flatVerts); flatVerts = bigger; }
    let o = flatCount * 6;
    for (const [x, y, c] of [[x1, y1, c1], [x2, y2, c2], [x3, y3, c3]]) {
      flatVerts[o++] = x; flatVerts[o++] = y; flatVerts[o++] = c[0]; flatVerts[o++] = c[1]; flatVerts[o++] = c[2]; flatVerts[o++] = c[3];
    }
    flatCount += 3;
  }
  function pushQuad(p, c) { pushTri(p[0], p[1], p[2], p[3], p[4], p[5], c, c, c); pushTri(p[0], p[1], p[4], p[5], p[6], p[7], c, c, c); }
  const shade = (c, k) => [Math.min(1, c[0] * k), Math.min(1, c[1] * k), Math.min(1, c[2] * k), c[3]];

  /**
   * A cube of n x n x n blocks, turned by `yaw` and `pitch` (degrees; 45 and 35.26 is the corner-on isometric view)
   * and drawn flat (orthographic). Each block on the faces turned to the viewer can stand out along its face, pushed
   * by one band of the sound: the "visualizer cube". Faces are lit by one light, so they shade as it turns.
   */
  function drawCube(l, v, cx, cy, S, tl) {
    const n = clamp(Math.round(num(l.cells, 4)), 1, 8);
    const unit = prop(v, l, 'size', 0.4) * S * v.scale / n / 1.41;
    const gap = clamp(num(l.gap, 0.06), 0, 0.4);
    const top = color(l.top || '#f4b6d8'), left = color(l.left || '#c77dd6'), right = color(l.right || '#e895c0');
    const push = num(l.audio?.amount, 0);
    const yaw = prop(v, l, 'yaw', 45) * Math.PI / 180, pitch = prop(v, l, 'pitch', 35.264) * Math.PI / 180;
    const roll = v.rotation * Math.PI / 180;
    const cyw = Math.cos(yaw), syw = Math.sin(yaw), cp = Math.cos(pitch), sp = Math.sin(pitch), cr = Math.cos(roll), sr = Math.sin(roll);
    // yaw around y, then pitch around x; +z comes out of the screen
    const turn = (x, y, z) => {
      const x1 = x * cyw - z * syw, z1 = x * syw + z * cyw;
      const y2 = y * cp - z1 * sp, z2 = y * sp + z1 * cp;
      return [x1 * cr - y2 * sr, x1 * sr + y2 * cr, z2];
    };
    // perspective: a camera `perspective` cube-widths away (0 = flat, orthographic)
    const camD = num(l.perspective, 0) * n;
    const project = (p) => {
      const q = turn(...p);
      const f = camD > 0 ? camD / Math.max(0.1, camD - q[2]) : 1;
      return [cx + q[0] * unit * f, cy - q[1] * unit * f];
    };
    const light = (() => { const L = [-0.35, 0.75, 0.6], m = Math.hypot(...L); return L.map((c) => c / m); })();
    const lit = (c, nrm) => shade(c, 0.62 + 0.5 * Math.max(0, nrm[0] * light[0] + nrm[1] * light[1] + nrm[2] * light[2]));
    // optional colour across the whole cube on screen (two colours along a direction that turns), shaded per face
    const grad = l.gradient && Array.isArray(l.gradient.colors) ? l.gradient : null;
    const gA = grad ? color(grad.colors[0], [1, 1, 1, 1]) : null, gB = grad ? color(grad.colors[grad.colors.length - 1], [1, 1, 1, 1]) : null;
    const gAng = grad ? (num(grad.angle, 0) * Math.PI / 180 + TAU * Math.round(num(grad.turns, 0)) * (tl / LOOP)) : 0;
    const gSpan = n * unit * 0.9;
    const vcol = (sx, sy, k) => {
      const t = clamp(0.5 + ((sx - cx) * Math.cos(gAng) + (sy - cy) * Math.sin(gAng)) / (2 * gSpan), 0, 1);
      const c = [gA[0] + (gB[0] - gA[0]) * t, gA[1] + (gB[1] - gA[1]) * t, gA[2] + (gB[2] - gA[2]) * t, 1];
      return shade(c, k);
    };
    const quad = (pts, nrm, base, dim) => {
      const s = pts.map(project);
      if (!grad) { pushQuad(s.flat(), shade(lit(base, nrm), dim)); return; }
      const k = (0.62 + 0.5 * Math.max(0, nrm[0] * light[0] + nrm[1] * light[1] + nrm[2] * light[2])) * dim;
      const c = s.map(([x, y]) => vcol(x, y, k));
      pushTri(s[0][0], s[0][1], s[1][0], s[1][1], s[2][0], s[2][1], c[0], c[1], c[2]);
      pushTri(s[0][0], s[0][1], s[2][0], s[2][1], s[3][0], s[3][1], c[0], c[2], c[3]);
    };
    // a face shows when it turns towards the camera (with perspective, towards the camera's position)
    const facing = (tn, centre) => camD > 0
      ? tn[0] * -centre[0] + tn[1] * -centre[1] + tn[2] * (camD - centre[2]) > 0
      : tn[2] > 0;
    // the six faces of a box: outward normal, its colour, its four corners
    const faces = (x0, y0, z0, x1, y1, z1) => [
      [[0, 1, 0], top, [[x0, y1, z0], [x1, y1, z0], [x1, y1, z1], [x0, y1, z1]]],
      [[0, -1, 0], top, [[x0, y0, z0], [x0, y0, z1], [x1, y0, z1], [x1, y0, z0]]],
      [[1, 0, 0], right, [[x1, y0, z0], [x1, y0, z1], [x1, y1, z1], [x1, y1, z0]]],
      [[-1, 0, 0], right, [[x0, y0, z0], [x0, y1, z0], [x0, y1, z1], [x0, y0, z1]]],
      [[0, 0, 1], left, [[x0, y0, z1], [x0, y1, z1], [x1, y1, z1], [x1, y0, z1]]],
      [[0, 0, -1], left, [[x0, y0, z0], [x1, y0, z0], [x1, y1, z0], [x0, y1, z0]]],
    ];
    const drawBox = (box, dim) => {
      for (const [nrm, c, pts] of faces(...box)) {
        const tn = turn(...nrm);
        const mid = turn((pts[0][0] + pts[2][0]) / 2, (pts[0][1] + pts[2][1]) / 2, (pts[0][2] + pts[2][2]) / 2);
        if (!facing(tn, mid)) continue;
        quad(pts, tn, c, dim);
      }
    };
    const h = n / 2, lo = gap / 2, hi = 1 - gap / 2;
    drawBox([-h, -h, -h, h, h, h], 0.6);
    // blocks on the faces turned to the viewer, far ones first
    const boxes = [];
    let band = 0;
    for (const [axis, sign] of [[1, 1], [1, -1], [0, 1], [0, -1], [2, 1], [2, -1]]) {
      const nrm = [0, 0, 0]; nrm[axis] = sign;
      if (!facing(turn(...nrm), turn(nrm[0] * h, nrm[1] * h, nrm[2] * h))) { band += n * n; continue; }
      const [ua, ub] = [0, 1, 2].filter((k) => k !== axis);
      for (let a = 0; a < n; a++)
        for (let b = 0; b < n; b++) {
          const e = push * level(Math.floor((band++ * 5) % 44)) * n;
          const p0 = [0, 0, 0], p1 = [0, 0, 0];
          p0[ua] = a + lo - h; p1[ua] = a + hi - h;
          p0[ub] = b + lo - h; p1[ub] = b + hi - h;
          if (sign > 0) { p0[axis] = h; p1[axis] = h + 0.02 + e; } else { p0[axis] = -h - 0.02 - e; p1[axis] = -h; }
          const mid = turn((p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2, (p0[2] + p1[2]) / 2);
          boxes.push([mid[2], [...p0, ...p1]]);
        }
    }
    boxes.sort((p, q) => p[0] - q[0]);
    for (const [, box] of boxes) drawBox(box, 1);
  }

  /** A soft dot as a fan: full in the middle, gone at the rim. */
  function pushDot(x, y, r, c) {
    const rim = [c[0], c[1], c[2], 0];
    for (let k = 0; k < 10; k++) {
      const a0 = k / 10 * TAU, a1 = (k + 1) / 10 * TAU;
      pushTri(x, y, x + Math.cos(a0) * r, y + Math.sin(a0) * r, x + Math.cos(a1) * r, y + Math.sin(a1) * r, c, rim, rim);
    }
  }
  /** A line through the points, `w` to each side, full on the centre line and gone at the edges. */
  function pushLine(pts, w, c) {
    const side = [c[0], c[1], c[2], 0];
    for (let k = 0; k < pts.length - 1; k++) {
      const [x0, y0] = pts[k], [x1, y1] = pts[k + 1];
      const len = Math.hypot(x1 - x0, y1 - y0) || 1, nx = -(y1 - y0) / len * w, ny = (x1 - x0) / len * w;
      for (const s of [1, -1]) {
        pushTri(x0, y0, x1, y1, x0 + nx * s, y0 + ny * s, c, c, side);
        pushTri(x1, y1, x1 + nx * s, y1 + ny * s, x0 + nx * s, y0 + ny * s, c, side, side);
      }
    }
  }
  function hsl(h, s, li) {
    const a = s * Math.min(li, 1 - li), f = (n) => { const k = (n + h / 30) % 12; return li - a * Math.max(-1, Math.min(k - 3, 9 - k, 1)); };
    return [f(0), f(8), f(4), 1];
  }
  let tentaclePrev = null;
  /**
   * The "tentacles" style: dots fixed over the screen, each with a reach of its own; every dot that can reach the
   * cursor throws a thin curved line to it (each line its own hue, cyan to blue, added as light) and turns white,
   * the rest stay small dark cyan specks; a solid light-blue disc sits on the cursor, bigger while it moves. With no
   * mouse the cursor runs a figure eight (a lemniscate) a whole number of times per loop.
   */
  function drawTentacleField(l, v, S, BX, BY, BW, BH, offX, offY, tl, ph) {
    const count = clamp(Math.round(num(l.count, 500)), 1, 3000);
    const px = S / 1440;   // the original's pixel sizes are for a 1440-high screen
    const minR = num(l.minReach, 0.035) * S, maxR = num(l.maxReach, 0.21) * S;
    const boost = 1 + num(l.audio?.amount, 0) * level(l.audio?.band ?? 'bass');
    // the disc grows with how far the cursor moves per step of the original (it stepped about 120 times a second)
    let fx, fy, moved;
    if (pointer.seen) {
      fx = (pointer.tx * 0.5 + 0.5) * canvas.width; fy = (pointer.ty * 0.5 + 0.5) * canvas.height;
      moved = tentaclePrev ? Math.hypot(fx - tentaclePrev[0], fy - tentaclePrev[1]) / 2 : 0;
      tentaclePrev = [fx, fy];
    } else {
      const turns = Math.max(1, Math.round(num(l.turns, 10))), t = TAU * turns * ph, a = BH / 2 - 10 * px;
      const at = (q) => { const den = Math.sin(q) ** 2 + 1; return [a * Math.SQRT2 * Math.cos(q) / den, a * Math.SQRT2 * Math.cos(q) * Math.sin(q) / den]; };
      const p0 = at(t), p1 = at(t + 0.001);
      fx = BX + BW / 2 + p0[0]; fy = BY + BH / 2 + p0[1];
      // per step: the path's speed times the original's 0.01 radians a step
      moved = Math.hypot(p1[0] - p0[0], p1[1] - p0[1]) / 0.001 * 0.01;
    }
    // its target trails the cursor by a tenth a step, so the disc measured about twice the step
    moved = Math.min(50 * px, moved * 2);
    const lines = [];
    for (let k = 0; k < count; k++) {
      const r = hash(k, 31);
      const x = BX + hash(k, 21) * BW + offX, y = BY + hash(k, 22) * BH + offY;
      const reach = (minR + (maxR - minR) * hash(k, 32)) * boost;
      const d = Math.hypot(fx - x, fy - y);
      if (d <= reach) {
        lines.push([x, y, d, reach, r, k]);
        pushDisc(x, y, (2 * r + 1) * px, [1, 1, 1, 1]);
      } else if (r > 0.05) pushDisc(x, y, r * 2 * px, [0, 0.545, 0.545, 1]);
    }
    pushDisc(fx, fy, 5 * px + moved, [0.6, 0.8, 1, 1]);
    for (const [x, y, d, reach, r, k] of lines) {
      // the slack (reach longer than the distance) bends the line into an S that curls in near the cursor
      const ux = (fx - x) / (d || 1), uy = (fy - y) / (d || 1);
      const slack = Math.min(1.4, (reach - d) / Math.max(1, d) + 0.25), side = hash(k, 33) < 0.5 ? 1 : -1;
      const a1 = side * (0.5 + 0.9 * hash(k, 34)) * slack, a2 = -side * (0.8 + 1.2 * hash(k, 35)) * slack;
      const c1 = [x + (ux * Math.cos(a1) - uy * Math.sin(a1)) * reach * 0.45, y + (ux * Math.sin(a1) + uy * Math.cos(a1)) * reach * 0.45];
      const c2 = [fx - (ux * Math.cos(a2) - uy * Math.sin(a2)) * reach * 0.35, fy - (ux * Math.sin(a2) + uy * Math.cos(a2)) * reach * 0.35];
      // plus a wave along it, so the line wiggles two or three times on its way like the original's chains
      const wAmp = reach * 0.07 * slack * (0.5 + hash(k, 36)), wPh = hash(k, 37), wN = 0.5 + 0.7 * hash(k, 38);
      const pts = [];
      for (let s = 0; s <= 24; s++) {
        const t = s / 24, m = 1 - t;
        const bx = m * m * m * x + 3 * m * m * t * c1[0] + 3 * m * t * t * c2[0] + t * t * t * fx;
        const by = m * m * m * y + 3 * m * m * t * c1[1] + 3 * m * t * t * c2[1] + t * t * t * fy;
        const off = wAmp * Math.sin(TAU * (wN * t + wPh)) * Math.sin(Math.PI * t);
        pts.push([bx - uy * off, by + ux * off]);
      }
      pushLine(pts, Math.max(0.5, r * 1.3) * px, hsl(r * 60 + 180, 1, r * 0.6 + 0.25));
    }
  }
  /** A solid disc, its rim softened over one pixel. */
  function pushDisc(x, y, r, c) {
    const rim = [c[0], c[1], c[2], 0];
    for (let k = 0; k < 16; k++) {
      const a0 = k / 16 * TAU, a1 = (k + 1) / 16 * TAU, c0 = Math.cos(a0), s0 = Math.sin(a0), c1 = Math.cos(a1), s1 = Math.sin(a1);
      pushTri(x, y, x + c0 * r, y + s0 * r, x + c1 * r, y + s1 * r, c, c, c);
      pushTri(x + c0 * r, y + s0 * r, x + c0 * (r + 1), y + s0 * (r + 1), x + c1 * r, y + s1 * r, c, rim, c);
      pushTri(x + c1 * r, y + s1 * r, x + c0 * (r + 1), y + s0 * (r + 1), x + c1 * (r + 1), y + s1 * (r + 1), c, rim, rim);
    }
  }
  /** A lightning path between two points by midpoint displacement; the same seed gives the same bolt. */
  function boltPoints(x0, y0, x1, y1, seed, rough) {
    let pts = [[x0, y0], [x1, y1]];
    let disp = Math.hypot(x1 - x0, y1 - y0) * rough;
    for (let lvl = 0; lvl < 5; lvl++) {
      const next = [pts[0]];
      for (let k = 0; k < pts.length - 1; k++) {
        const [ax, ay] = pts[k], [bx, by] = pts[k + 1];
        const len = Math.hypot(bx - ax, by - ay) || 1, off = (hash(seed, lvl, k) - 0.5) * 2 * disp;
        next.push([(ax + bx) / 2 - (by - ay) / len * off, (ay + by) / 2 + (bx - ax) / len * off], pts[k + 1]);
      }
      pts = next;
      disp *= 0.55;
    }
    return pts;
  }

  /**
   * Dots scattered over the screen; the current follows the mouse: bolts jump from the cursor to the nearest dots and
   * on from each one to its neighbour, a new shape `flicker` times a second. With no mouse (a video, a still) the
   * current wanders on a loop of its own.
   */
  function drawElectric(l, v, S, BX, BY, BW, BH, offX, offY, tl, ph) {
    if (l.style === 'tentacles') { drawTentacleField(l, v, S, BX, BY, BW, BH, offX, offY, tl, ph); return; }
    const count = clamp(Math.round(num(l.count, 120) * BW * BH / (1920 * 1080)), 1, 2000);
    const col = color(l.color || '#8fd0ff'), core = color(l.core || '#ffffff');
    const boost = 1 + num(l.audio?.amount, 0) * level(l.audio?.band ?? 'bass');
    const reach = prop(v, l, 'reach', 0.22) * S * boost;
    const dotR = num(l.size, 0.004) * S;
    let fx, fy;
    if (pointer.seen) { fx = (pointer.tx * 0.5 + 0.5) * canvas.width; fy = (pointer.ty * 0.5 + 0.5) * canvas.height; }
    else { fx = BX + BW * (0.5 + 0.32 * Math.sin(TAU * ph)); fy = BY + BH * (0.5 + 0.28 * Math.sin(TAU * 2 * ph + 1)); }
    const wob = num(l.wander, 0.01) * S;
    const nodes = [];
    for (let k = 0; k < count; k++) {
      const x = BX + hash(k, 21) * BW + offX + wob * Math.sin(TAU * ((1 + (k % 3)) * ph + hash(k, 23)));
      const y = BY + hash(k, 22) * BH + offY + wob * Math.cos(TAU * ((1 + (k % 2)) * ph + hash(k, 24)));
      nodes.push({ x, y, d: Math.hypot(x - fx, y - fy) });
    }
    for (const n of nodes) {
      const near = clamp(1 - n.d / (reach * 1.6), 0, 1);
      pushDot(n.x, n.y, dotR * (1 + near * 1.5), [col[0], col[1], col[2], col[3] * (0.25 + 0.75 * near)]);
    }
    const flick = Math.floor(tl * Math.max(1, num(l.flicker, 16)));
    const close = nodes.filter((n) => n.d < reach).sort((p, q) => p.d - q.d).slice(0, clamp(Math.round(num(l.arcs, 7)), 1, 24));
    const w = num(l.width, 0.0016) * S, rough = num(l.roughness, 0.22);
    close.forEach((n, k) => {
      const fade = 1 - n.d / reach, on = 0.55 + 0.45 * hash(flick, k, 3);
      const pts = boltPoints(fx, fy, n.x, n.y, flick * 97 + k, rough);
      pushLine(pts, w * 3, [col[0], col[1], col[2], 0.35 * fade * on]);
      pushLine(pts, w, [core[0], core[1], core[2], fade * on]);
      // on to the dot's nearest neighbour, so the current runs from dot to dot
      let best = null, bd = reach * 0.55;
      for (const m of nodes) { if (m === n) continue; const dd = Math.hypot(m.x - n.x, m.y - n.y); if (dd < bd) { bd = dd; best = m; } }
      if (best) {
        const bp = boltPoints(n.x, n.y, best.x, best.y, flick * 97 + k + 50, rough);
        pushLine(bp, w * 2.4, [col[0], col[1], col[2], 0.25 * fade * on]);
        pushLine(bp, w * 0.8, [core[0], core[1], core[2], 0.6 * fade * on]);
      }
    });
    if (close.length) pushDot(fx, fy, dotR * 3, [core[0], core[1], core[2], 0.8]);
  }

  /** A glowing body with tendrils curling behind it, each one a strip thinning to its tip. */
  function drawTendrils(l, v, cx, cy, S, ph) {
    const count = clamp(Math.round(num(l.count, 12)), 1, 48);
    const seg = clamp(Math.round(num(l.segments, 28)), 4, 64);
    const grow = 1 + num(l.audio?.amount, 0) * level(l.audio?.band ?? 'bass');
    const len = prop(v, l, 'length', 0.25) * S * v.scale * grow;
    const width = num(l.width, 0.006) * S;
    const curl = num(l.curl, 1.2);
    const k = Math.max(1, Math.round(num(l.waves, 2)));
    const col = color(l.color || '#9fd8ff');
    const heading = num(l.heading, 200) * Math.PI / 180;
    for (let j = 0; j < count; j++) {
      const r = hash(j, 11), spread = num(l.spread, 0.9);
      let ang = heading + (j / Math.max(1, count - 1) - 0.5) * spread * 2 + (r - 0.5) * 0.3;
      let x = cx, y = cy;
      const step = len * (0.6 + 0.6 * hash(j, 12)) / seg;
      let prevL = null, prevR = null, prevC = null;
      for (let m = 0; m <= seg; m++) {
        const f = m / seg;
        const sway = curl * f * Math.sin(TAU * (k * ph + f * 0.9 + r)) + 0.4 * curl * f * f * Math.sin(TAU * (2 * k * ph + f * 1.7 + r * 3));
        const a = ang + sway;
        if (m > 0) { x += Math.cos(a) * step; y += Math.sin(a) * step; }
        const w = width * (1 - f * 0.92);
        const nx = -Math.sin(a) * w, ny = Math.cos(a) * w;
        const c = [col[0], col[1], col[2], col[3] * (1 - f) * 0.9];
        const L = [x + nx, y + ny], R = [x - nx, y - ny];
        if (prevL) { pushTri(prevL[0], prevL[1], prevR[0], prevR[1], L[0], L[1], prevC, prevC, c); pushTri(prevR[0], prevR[1], R[0], R[1], L[0], L[1], prevC, c, c); }
        prevL = L; prevR = R; prevC = c;
      }
    }
  }

  // ------------------------------------------------------------------ drawing one frame
  const pointer = { x: 0, y: 0, tx: 0, ty: 0, seen: false };
  let lastDraw = 0;

  function draw(t) {
    const CW = canvas.width, CH = canvas.height;
    ensureTargets(CW, CH);
    const tl = ((t % LOOP) + LOOP) % LOOP, ph = tl / LOOP;
    if (FAKE_AUDIO) fakeAudio(t);
    // layout box: the monitor's part of the canvas (smaller than the canvas only when rendering with a pan margin)
    const BW = CW / (1 + 2 * MARGIN), BH = CH / (1 + 2 * MARGIN), BX = (CW - BW) / 2, BY = (CH - BH) / 2;
    const S = Math.min(BW, BH);
    const par = MODE === 'live' ? num(W.parallax, 0) : 0;

    bindTarget(T.a);
    const bg = color(W.background, [0, 0, 0, 1]);
    gl.clearColor(bg[0], bg[1], bg[2], 1);
    gl.clear(gl.COLOR_BUFFER_BIT);

    W.layers.forEach((l, i) => {
      l.__id = l.__id ?? i;
      const v = animated(l, tl);
      if (v.opacity <= 0) return;
      const depth = clamp(num(l.depth, 0), 0, 1);
      const offX = v.x * S - pointer.x * depth * par * S, offY = v.y * S - pointer.y * depth * par * S;
      const anchor = ANCHORS[l.anchor] || ANCHORS.center;
      const ax = BX + anchor[0] * BW, ay = BY + anchor[1] * BH;
      setBlend(l.blend);
      if (l.type === 'image') {
        const img = images.get(l.src);
        if (!img) return;
        const fit = l.fit || 'cover';
        const sway = depth * par * S;   // how far the mouse can push this layer
        let dw, dh;
        if (fit === 'cover') { const k = Math.max((CW + 2 * sway) / img.w, (CH + 2 * sway) / img.h); dw = img.w * k; dh = img.h * k; }
        else if (fit === 'contain') { const k = Math.min(BW / img.w, BH / img.h); dw = img.w * k; dh = img.h * k; }
        else if (fit === 'width') { dw = BW; dh = img.h * BW / img.w; }
        else if (fit === 'height') { dh = BH; dw = img.w * BH / img.h; }
        else { dh = num(l.size, 0.5) * S; dw = img.w * dh / img.h; }
        dw *= v.scale; dh *= v.scale;
        // the image's own anchor point sits on the screen's (cover anchors to the whole canvas)
        const refX = fit === 'cover' ? anchor[0] * CW : ax, refY = fit === 'cover' ? anchor[1] * CH : ay;
        let cx = refX + (0.5 - anchor[0]) * dw + offX, cy = refY + (0.5 - anchor[1]) * dh + offY;
        // An image held to an edge (mountains on the bottom) keeps its edge row going past the screen's edge, so the
        // parallax and the pan margin never open a gap under it.
        let uv = [0, 0, 1, 1], qw = dw, qh = dh;
        if (fit !== 'cover' && v.rotation === 0) {
          const bleedX = BX + sway + 0.05 * S, bleedY = BY + sway + 0.05 * S;
          const ext = [anchor[0] === 0 ? bleedX : 0, anchor[1] === 0 ? bleedY : 0, anchor[0] === 1 ? bleedX : 0, anchor[1] === 1 ? bleedY : 0];
          qw = dw + ext[0] + ext[2]; qh = dh + ext[1] + ext[3];
          cx += (ext[2] - ext[0]) / 2; cy += (ext[3] - ext[1]) / 2;
          uv = [-ext[0] / dw, -ext[1] / dh, qw / dw, qh / dh];
        }
        gl.useProgram(P.image.p);
        gl.uniformMatrix3fv(P.image.u.u_m, false, rectMatrix(cx, cy, qw, qh, v.rotation, CW, CH));
        gl.uniform4fv(P.image.u.u_uv, uv);
        tex(0, img);
        // scrolling wraps the image around: a whole number of wraps per loop on each axis
        const scroll = Array.isArray(l.scroll) ? l.scroll : null;
        const wrap = scroll ? gl.REPEAT : gl.CLAMP_TO_EDGE;
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, wrap);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, wrap);
        gl.uniform2f(P.image.u.u_scroll, scroll ? Math.round(num(scroll[0], 0)) * ph : 0, scroll ? Math.round(num(scroll[1], 0)) * ph : 0);
        gl.uniform1i(P.image.u.u_tex, 0);
        const rp = l.ripple && images.get(l.ripple.src);
        gl.uniform1i(P.image.u.u_ripple, rp ? 1 : 0);
        if (rp) {
          // the two samples drift apart; on a circle (same speed, `drift` in image widths per second) so the loop closes
          const tiles = Array.isArray(l.ripple.tiles) ? l.ripple.tiles : [1, 1];
          const R = num(l.ripple.drift, 0.002) * Math.SQRT2 * LOOP / TAU;
          const dx = R * (Math.cos(TAU * ph) - 1), dy = R * Math.sin(TAU * ph);
          tex(1, rp);
          gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.REPEAT);
          gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.REPEAT);
          gl.uniform1i(P.image.u.u_normal, 1);
          gl.uniform2f(P.image.u.u_tiles, num(tiles[0], 1), num(tiles[1], 1));
          gl.uniform4f(P.image.u.u_drift, dx, dy, -dx, -dy);
          gl.uniform1f(P.image.u.u_ramount, prop(v, l, 'rippleAmount', num(l.ripple.amount, 0.005)));
        } else gl.uniform1i(P.image.u.u_normal, 0);
        // masks: an image of their own sliding a whole number of wraps per loop, read by their red channel
        const maskOf = (m, unit, on, sampler, scrollU) => {
          const mi = m && images.get(m.src);
          gl.uniform1i(P.image.u[on], mi ? 1 : 0);
          gl.uniform1i(P.image.u[sampler], mi ? unit : 0);
          if (!mi) return;
          tex(unit, mi);
          gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.REPEAT);
          gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.REPEAT);
          const s = Array.isArray(m.scroll) ? m.scroll : [0, 0];
          gl.uniform2f(P.image.u[scrollU], Math.round(num(s[0], 0)) * ph, Math.round(num(s[1], 0)) * ph);
        };
        maskOf(l.mask, 2, 'u_maskOn', 'u_mask', 'u_mscroll');
        maskOf(l.colorMask, 3, 'u_tmOn', 'u_tmask', 'u_tmscroll');
        if (l.colorMask) { const tc = color(l.colorMask.color, [1, 1, 1, 1]); gl.uniform3f(P.image.u.u_tmcolor, tc[0], tc[1], tc[2]); }
        const lt = l.light && typeof l.light === 'object' ? l.light : null;
        gl.uniform1i(P.image.u.u_lightOn, lt ? 1 : 0);
        if (lt) {
          gl.uniform3f(P.image.u.u_lpos, num(lt.x, 1), num(lt.y, 0), Math.max(0.001, num(lt.height, 0.25)));
          gl.uniform3f(P.image.u.u_light, num(lt.ambient, 0.5), prop(v, l, 'lightIntensity', num(lt.intensity, 0.5)), num(lt.radius, 1));
          gl.uniform1f(P.image.u.u_aspect, img.w / img.h);
        }
        gl.uniform4fv(P.image.u.u_tint, color(l.tint, [1, 1, 1, 1]));
        gl.uniform1f(P.image.u.u_opacity, v.opacity);
        gl.drawArrays(gl.TRIANGLES, 0, 6);
      } else if (l.type === 'text') {
        // Typewriter: the text appears letter by letter over `reveal` seconds (snapped to divide the loop), then holds.
        const px = Math.max(4, Math.round(num(l.size, 0.03) * S * v.scale));
        const total = totalChars(l);
        let chars = total;
        if (l._reveal > 0) {
          const f = (tl % l._reveal) / l._reveal;
          chars = Math.floor(total * clamp(f / 0.75, 0, 1));   // writes in the first three quarters, holds the rest
        }
        const blink = l.cursor ? Math.floor(tl * 2) % 2 === 0 : false;
        const tx = textTexture(l, px, chars, blink);
        const cx = ax + (0.5 - anchor[0]) * tx.w + offX, cy = ay + (0.5 - anchor[1]) * tx.h + offY;
        gl.useProgram(P.image.p);
        gl.uniformMatrix3fv(P.image.u.u_m, false, rectMatrix(cx, cy, tx.w, tx.h, v.rotation, CW, CH));
        gl.uniform4fv(P.image.u.u_uv, [0, 0, 1, 1]);
        tex(0, tx);
        gl.uniform1i(P.image.u.u_tex, 0);
        gl.uniform1i(P.image.u.u_ripple, 0);
        gl.uniform1i(P.image.u.u_normal, 0);
        gl.uniform2f(P.image.u.u_scroll, 0, 0);
        gl.uniform1i(P.image.u.u_maskOn, 0);
        gl.uniform1i(P.image.u.u_mask, 0);
        gl.uniform1i(P.image.u.u_tmOn, 0);
        gl.uniform1i(P.image.u.u_tmask, 0);
        gl.uniform1i(P.image.u.u_lightOn, 0);
        gl.uniform4fv(P.image.u.u_tint, [1, 1, 1, 1]);
        gl.uniform1f(P.image.u.u_opacity, v.opacity);
        gl.drawArrays(gl.TRIANGLES, 0, 6);
      } else if (l.type === 'gradient') {
        const stops = (Array.isArray(l.stops) ? l.stops : []).slice(0, 8).filter((s) => Array.isArray(s));
        if (stops.length === 0) return;
        const cols = new Float32Array(32), pos = new Float32Array(8);
        stops.forEach((s, k) => { cols.set(color(s[1]), k * 4); pos[k] = num(s[0], k / Math.max(1, stops.length - 1)); });
        const radial = l.kind === 'radial';
        const ang = num(l.angle, 90) * Math.PI / 180, dir = [Math.cos(ang), Math.sin(ang)];
        const corners = [[0, 0], [CW, 0], [0, CH], [CW, CH]].map((p) => p[0] * dir[0] + p[1] * dir[1]);
        const center = Array.isArray(l.center) ? l.center : [0.5, 0.5];
        gl.useProgram(P.gradient.p);
        const u = P.gradient.u;
        gl.uniform2f(u.u_res, CW, CH);
        gl.uniform2f(u.u_offset, offX, offY);
        gl.uniform1i(u.u_kind, radial ? 1 : 0);
        gl.uniform2f(u.u_dir, dir[0], dir[1]);
        gl.uniform2f(u.u_range, Math.min(...corners), Math.max(...corners));
        gl.uniform2f(u.u_center, BX + num(center[0], 0.5) * BW, BY + num(center[1], 0.5) * BH);
        gl.uniform1f(u.u_radius, num(l.radius, 0.8) * S * v.scale);
        gl.uniform1f(u.u_opacity, v.opacity);
        gl.uniform4fv(u.u_col, cols);
        gl.uniform1fv(u.u_pos, pos);
        gl.uniform1i(u.u_n, stops.length);
        gl.drawArrays(gl.TRIANGLES, 0, 6);
      } else if (l.type === 'shape' && l.shape === 'polygon') {
        // points in s around the anchor; filled as a fan (convex shapes)
        const pts = (Array.isArray(l.points) ? l.points : []).filter((p) => Array.isArray(p) && p.length === 2);
        if (pts.length < 3) return;
        const r = v.rotation * Math.PI / 180, c0 = Math.cos(r), s0 = Math.sin(r), sc = S * v.scale;
        const xy = pts.map(([px, py]) => [ax + offX + (px * c0 - py * s0) * sc, ay + offY + (px * s0 + py * c0) * sc]);
        const col = color(l.color, [1, 1, 1, 1]);
        flatCount = 0;
        for (let k = 1; k < xy.length - 1; k++) pushTri(xy[0][0], xy[0][1], xy[k][0], xy[k][1], xy[k + 1][0], xy[k + 1][1], col, col, col);
        drawFlat(flatVerts, flatCount, CW, CH, v.opacity);
      } else if (l.type === 'shape') {
        const kind = l.shape === 'ring' ? 1 : l.shape === 'rect' ? 2 : 0;
        const w = prop(v, l, 'size', 0.1) * S * v.scale, h = kind === 2 ? prop(v, l, 'height', num(l.size, 0.1)) * S * v.scale : w;
        const soft = clamp(num(l.softness, 0), 0, 1) * Math.max(w, h) * 0.5;
        const box = [w + soft * 2 + 4, h + soft * 2 + 4];
        gl.useProgram(P.shape.p);
        const u = P.shape.u;
        gl.uniformMatrix3fv(u.u_m, false, rectMatrix(ax + offX, ay + offY, box[0], box[1], v.rotation, CW, CH));
        gl.uniform2f(u.u_box, box[0], box[1]);
        gl.uniform1i(u.u_shape, kind);
        gl.uniform2f(u.u_half, w / 2, h / 2);
        gl.uniform1f(u.u_thick, num(l.thickness, 0.01) * S);
        gl.uniform1f(u.u_soft, soft);
        gl.uniform4fv(u.u_color, color(l.color, [1, 1, 1, 1]));
        gl.uniform1f(u.u_opacity, v.opacity);
        gl.drawArrays(gl.TRIANGLES, 0, 6);
      } else if (l.type === 'particles') {
        const preset = PARTICLES[l.preset];
        if (!preset) return;
        const count = Math.min(20000, Math.round(preset[1] * Math.max(0, prop(v, l, 'density', 1)) * (CW * CH) / (1920 * 1080)));
        if (count <= 0) return;
        if ((l.preset === 'fireflies' || l.preset === 'streaks') && !l.blend) setBlend('add');
        const pal = Array.isArray(l.colors) ? palette(l.colors, 6) : { data: new Float32Array(24), count: 0 };
        gl.useProgram(P.particles.p);
        const u = P.particles.u;
        gl.uniform2f(u.u_res, CW, CH);
        gl.uniform1f(u.u_s, S);
        gl.uniform1f(u.u_ph, ph);
        gl.uniform1f(u.u_L, LOOP);
        gl.uniform1i(u.u_preset, preset[0]);
        gl.uniform1f(u.u_seed, num(l.seed, i + 1));
        gl.uniform1f(u.u_size, num(l.size, l.preset === 'rain' ? 0.0015 : l.preset === 'snow' ? 0.006 : l.preset === 'streaks' ? 0.003 : 0.004) * v.scale);
        gl.uniform1f(u.u_speed, Math.max(0.05, prop(v, l, 'speed', 1)));
        gl.uniform1f(u.u_angle, num(l.angle, l.preset === 'streaks' ? -25 : 8));
        gl.uniform1f(u.u_len, prop(v, l, 'length', 0.35));
        gl.uniform4fv(u.u_cols, pal.data);
        gl.uniform1i(u.u_ncols, pal.count);
        gl.uniform1i(u.u_colorX, l.colorBy === 'x' ? 1 : 0);
        gl.uniform1f(u.u_shift, Math.round(num(l.colorShift, 0)) * ph);   // whole slides per loop
        gl.uniform1f(u.u_tips, clamp(num(l.tips, 0), 0, 1));
        gl.uniform1f(u.u_spread, clamp(num(l.speedSpread, 0.4), 0, 0.95));
        const thick = Array.isArray(l.thickness) ? l.thickness : [0.6, 1.4];
        gl.uniform2f(u.u_thick, num(thick[0], 0.6), num(thick[1], 1.4));
        gl.uniform1i(u.u_pill, l.shape === 'pill' ? 1 : 0);
        gl.uniform1f(u.u_lspread, clamp(num(l.lengthSpread, 1), 0, 1.9));
        gl.uniform1f(u.u_tipLen, clamp(num(l.tipLength, 0.18), 0.005, 1));
        gl.uniform1f(u.u_tailPow, Math.max(0.1, num(l.tail, 1.6)));
        gl.uniform1i(u.u_flat, l.profile === 'flat' ? 1 : 0);
        gl.uniform2f(u.u_offset, offX, offY);
        gl.uniform4fv(u.u_color, color(l.color, l.preset === 'fireflies' ? [1, 0.85, 0.4, 1] : [1, 1, 1, 1]));
        gl.uniform1f(u.u_opacity, v.opacity);
        gl.drawArraysInstanced(gl.TRIANGLES, 0, 6, count);
      } else if (l.type === 'code') {
        const glyphs = glyphAtlas(typeof l.glyphs === 'string' && l.glyphs.length ? l.glyphs : MATRIX_GLYPHS);
        layerPass(P.code, (u) => {
          gl.uniform2f(u.u_res, CW, CH);
          gl.uniform2f(u.u_offset, offX, offY);
          gl.uniform1f(u.u_cell, Math.max(4, num(l.size, 0.018) * S * v.scale));
          gl.uniform1f(u.u_ph, ph);
          gl.uniform1f(u.u_L, LOOP);
          gl.uniform1f(u.u_seed, num(l.seed, i + 1));
          gl.uniform1f(u.u_speed, Math.max(0.05, prop(v, l, 'speed', 1)));
          gl.uniform1f(u.u_trail, Math.max(2, num(l.trail, 18)));
          gl.uniform1f(u.u_density, clamp(prop(v, l, 'density', 0.6), 0, 1));
          gl.uniform1f(u.u_change, Math.max(1, Math.round(num(l.changes, 40))));
          gl.uniform4fv(u.u_color, color(l.color, [0.1, 1, 0.35, 1]));
          gl.uniform4fv(u.u_head, color(l.head, [0.85, 1, 0.9, 1]));
          tex(0, glyphs); gl.uniform1i(u.u_atlas, 0);
          gl.uniform1f(u.u_glyphs, glyphs.count);
          gl.uniform1f(u.u_opacity, v.opacity);
        });
      } else if (l.type === 'wave') {
        const waves = (Array.isArray(l.waves) && l.waves.length ? l.waves : [{}]).slice(0, 3);
        const amp = [0, 0, 0], wl = [1, 1, 1], kk = [0, 0, 0], phs = [0, 0, 0];
        const ampScale = prop(v, l, 'amplitude', 1);
        waves.forEach((w, k) => {
          amp[k] = num(w.amplitude, 0.04) * S * ampScale;
          wl[k] = Math.max(0.01, num(w.length, 0.5)) * S;
          kk[k] = cycles(num(w.period, LOOP));
          phs[k] = num(w.phase, k * 1.7) * TAU;
        });
        const center = Array.isArray(l.center) ? l.center : [0.5, 0.5];
        layerPass(P.wave, (u) => {
          gl.uniform2f(u.u_res, CW, CH);
          gl.uniform2f(u.u_offset, offX, offY);
          gl.uniform2f(u.u_center, BX + num(center[0], 0.5) * BW, BY + num(center[1], 0.5) * BH);
          gl.uniform1f(u.u_angle, (num(l.angle, 0) + v.rotation) * Math.PI / 180);
          gl.uniform1f(u.u_base, prop(v, l, 'level', 0) * S);
          gl.uniform3fv(u.u_amp, amp); gl.uniform3fv(u.u_wl, wl); gl.uniform3fv(u.u_k, kk); gl.uniform3fv(u.u_phase, phs);
          gl.uniform1f(u.u_ph, ph);
          gl.uniform4fv(u.u_color, color(l.color, [1, 1, 1, 1]));
          gl.uniform4fv(u.u_color2, color(l.color2 || l.color, [1, 1, 1, 1]));
          gl.uniform1f(u.u_grad, num(l.gradient, 0.4) * S);
          gl.uniform1f(u.u_shadow, clamp(num(l.shadow, 0.35), 0, 1));
          gl.uniform1f(u.u_shadowW, num(l.shadowSize, 0.02) * S);
          gl.uniform1f(u.u_opacity, v.opacity);
        });
      } else if (l.type === 'noise') {
        const pal = palette(l.colors || ['#000000', '#ffffff'], 4);
        if (pal.count < 2) { pal.data.set(pal.data.subarray(0, 4), 4); pal.count = 2; }
        const lightAng = num(l.light, 135) * Math.PI / 180;
        layerPass(P.noise, (u) => {
          gl.uniform2f(u.u_res, CW, CH);
          gl.uniform2f(u.u_offset, offX, offY);
          gl.uniform1f(u.u_scale, Math.max(2, num(l.size, 0.25) * S * v.scale));
          gl.uniform1f(u.u_ph, ph);
          gl.uniform1f(u.u_drift, prop(v, l, 'drift', 0.4));
          gl.uniform1i(u.u_oct, clamp(Math.round(num(l.octaves, 5)), 1, 7));
          gl.uniform1i(u.u_ridged, l.ridged ? 1 : 0);
          gl.uniform1f(u.u_seed, num(l.seed, i + 1) * 17.13);
          gl.uniform1f(u.u_relief, prop(v, l, 'relief', 6));
          gl.uniform2f(u.u_light, Math.cos(lightAng), Math.sin(lightAng));
          gl.uniform4fv(u.u_col, pal.data);
          gl.uniform1i(u.u_ncol, pal.count);
          gl.uniform1f(u.u_contrast, num(l.contrast, 1.4));
          gl.uniform1f(u.u_shine, prop(v, l, 'shine', 0));
          gl.uniform1f(u.u_opacity, v.opacity);
        });
      } else if (l.type === 'water') {
        // light: an azimuth (0 = straight ahead, into the distance) and an elevation, in degrees
        const az = num(l.light, 0) * Math.PI / 180, el = num(l.lightHeight, 25) * Math.PI / 180;
        layerPass(P.water, (u) => {
          gl.uniform2f(u.u_res, CW, CH);
          gl.uniform2f(u.u_offset, offX, offY);
          gl.uniform1f(u.u_scale, Math.max(2, prop(v, l, 'size', 0.12) * S * v.scale / TAU));
          gl.uniform1f(u.u_ph, ph);
          gl.uniform1f(u.u_horizon, Math.max(0.05, num(l.horizon, 0.6)));
          gl.uniform1f(u.u_angle, (90 + num(l.angle, 0)) * Math.PI / 180);
          gl.uniform1f(u.u_spread, clamp(num(l.spread, 50), 0, 180) * Math.PI / 180);
          gl.uniform1f(u.u_height, prop(v, l, 'height', 0.7));
          gl.uniform1f(u.u_choppy, prop(v, l, 'choppy', 1));
          gl.uniform1f(u.u_speed, Math.max(0, prop(v, l, 'speed', 2)));
          gl.uniform1i(u.u_iter, clamp(Math.round(num(l.detail, 14)), 1, 24));
          gl.uniform1f(u.u_seed, hash(num(l.seed, i + 1), 77));
          gl.uniform3f(u.u_light, Math.sin(az) * Math.cos(el), Math.sin(el), Math.cos(az) * Math.cos(el));
          gl.uniform4fv(u.u_deep, color(l.deep, [0.02, 0.02, 0.02, 1]));
          gl.uniform4fv(u.u_crest, color(l.crest, [0.12, 0.12, 0.12, 1]));
          gl.uniform4fv(u.u_sky, color(l.sky, [0.15, 0.15, 0.15, 1]));
          gl.uniform1f(u.u_shine, prop(v, l, 'shine', 0.5));
          gl.uniform1f(u.u_gloss, Math.max(1, num(l.gloss, 40)));
          gl.uniform1f(u.u_sand, clamp(prop(v, l, 'sand', 0), 0, 1));
          gl.uniform1f(u.u_sandSize, Math.max(1, num(l.sandSize, 0.0015) * S));
          gl.uniform1f(u.u_fade, num(l.fade, 0.6));
          gl.uniform1f(u.u_opacity, v.opacity);
        });
      } else if (l.type === 'electric') {
        if (!l.blend) setBlend('add');
        flatCount = 0;
        drawElectric(l, v, S, BX, BY, BW, BH, offX, offY, tl, ph);
        drawFlat(flatVerts, flatCount, CW, CH, v.opacity);
      } else if (l.type === 'spectrum') {
        const n = clamp(Math.round(num(l.bars, 48)), 2, 256);
        const circle = l.shape === 'circle';
        const from = num(l.from, 0), to = num(l.to, 48);
        const spec = new Float32Array(64);
        const gain = num(l.gain, 1);
        for (let k = 0; k < 64; k++) spec[k] = spectrum[k] * gain;
        if (!l.blend) setBlend('add');
        gl.useProgram(P.bars.p);
        const u = P.bars.u;
        gl.uniform2f(u.u_res, CW, CH);
        gl.uniform1fv(u.u_spec, spec);
        gl.uniform1f(u.u_n, n);
        gl.uniform1i(u.u_mode, circle ? 1 : 0);
        gl.uniform2f(u.u_origin, ax + offX, ay + offY);
        gl.uniform1f(u.u_span, prop(v, l, 'width', 0.8) * S * v.scale);
        gl.uniform1f(u.u_barW, Math.max(1, num(l.barWidth, 0.006) * S * v.scale));
        gl.uniform1f(u.u_maxH, prop(v, l, 'height', 0.2) * S * v.scale);
        gl.uniform1f(u.u_minH, Math.max(1, num(l.minHeight, 0.004) * S));
        gl.uniform1f(u.u_radius, prop(v, l, 'radius', 0.15) * S * v.scale);
        gl.uniform1f(u.u_from, from);
        gl.uniform1f(u.u_to, to);
        gl.uniform1i(u.u_mirror, l.mirror ? 1 : 0);
        gl.uniform1f(u.u_rot, v.rotation * Math.PI / 180);
        const pal = (Array.isArray(l.colors) ? l.colors : [l.color || '#ffffff']);
        gl.uniform4fv(u.u_col0, color(pal[0], [1, 1, 1, 1]));
        gl.uniform4fv(u.u_col1, color(pal[pal.length - 1], [1, 1, 1, 1]));
        gl.uniform1f(u.u_opacity, v.opacity);
        gl.drawArraysInstanced(gl.TRIANGLES, 0, 6, n);
      } else if (l.type === 'cube') {
        flatCount = 0;
        drawCube(l, v, ax + offX, ay + offY, S, tl);
        drawFlat(flatVerts, flatCount, CW, CH, v.opacity);
      } else if (l.type === 'tendrils') {
        if (!l.blend) setBlend('add');
        flatCount = 0;
        drawTendrils(l, v, ax + offX, ay + offY, S, ph);
        drawFlat(flatVerts, flatCount, CW, CH, v.opacity);
      }
    });

    // post effects, ping-ponging between the two full targets
    let src = T.a, dst = T.b;
    const swap = () => { const s = src; src = dst; dst = s; };
    for (const e of W.effects) {
      if (e.type === 'blur') {
        const r = num(e.radius, 0.004) * S / 7;
        full(P.blur, (u) => { tex(0, src); gl.uniform1i(u.u_tex, 0); gl.uniform2f(u.u_step, r / CW, 0); }, dst); swap();
        full(P.blur, (u) => { tex(0, src); gl.uniform1i(u.u_tex, 0); gl.uniform2f(u.u_step, 0, r / CH); }, dst); swap();
      } else if (e.type === 'grade') {
        full(P.grade, (u) => {
          tex(0, src); gl.uniform1i(u.u_tex, 0);
          gl.uniform1f(u.u_bright, num(e.brightness, 0)); gl.uniform1f(u.u_contrast, num(e.contrast, 1));
          gl.uniform1f(u.u_sat, num(e.saturation, 1)); gl.uniform1f(u.u_hue, num(e.hue, 0) * Math.PI / 180);
          gl.uniform4fv(u.u_tint, color(e.tint, [1, 1, 1, 1])); gl.uniform1f(u.u_tintAmount, num(e.tintAmount, 0));
        }, dst); swap();
      } else if (e.type === 'grain') {
        // Live: a new grain every 1/24 s, the same sequence every loop. Rendered to video: one fixed grain, because
        // noise that changes in every pixel is the worst case for an encoder (measured: 61 -> 8.6 Mbit/s on Neve).
        const frame = MODE === 'render' ? 0 : Math.floor(tl * 24) % Math.max(1, Math.round(LOOP * 24));
        full(P.grain, (u) => {
          tex(0, src); gl.uniform1i(u.u_tex, 0); gl.uniform1f(u.u_amount, num(e.amount, 0.05)); gl.uniform1f(u.u_frame, frame); gl.uniform2f(u.u_res, CW, CH);
          gl.uniform1i(u.u_mult, e.mode === 'multiply' ? 1 : 0);
        }, dst); swap();
      } else if (e.type === 'glow' || e.type === 'rays') {
        const glow = e.type === 'glow';
        const boost = e.band != null ? 1 + num(e.bandAmount, 1) * level(e.band) : 1;
        full(P.threshold, (u) => { tex(0, src); gl.uniform1i(u.u_tex, 0); gl.uniform1f(u.u_threshold, num(e.threshold, glow ? 0.7 : 0.6)); }, T.c);
        if (glow) {
          const r = num(e.radius, 0.012) * S / 7 / 2;
          full(P.blur, (u) => { tex(0, T.c); gl.uniform1i(u.u_tex, 0); gl.uniform2f(u.u_step, r / T.c.w, 0); }, T.d);
          full(P.blur, (u) => { tex(0, T.d); gl.uniform1i(u.u_tex, 0); gl.uniform2f(u.u_step, 0, r / T.c.h); }, T.c);
        } else {
          // the light source is placed like a layer: an anchor plus an offset in s, so it follows an anchored moon
          const an = ANCHORS[e.anchor] || ANCHORS.center;
          const lx = (BX + an[0] * BW + num(e.x, 0) * S) / CW, ly = 1 - (BY + an[1] * BH + num(e.y, 0) * S) / CH;
          full(P.rays, (u) => { tex(0, T.c); gl.uniform1i(u.u_tex, 0); gl.uniform2f(u.u_light, lx, ly); gl.uniform1f(u.u_length, clamp(num(e.length, 0.4), 0, 1)); }, T.d);
          full(P.copy, (u) => { tex(0, T.d); gl.uniform1i(u.u_tex, 0); }, T.c);
        }
        full(P.add, (u) => { tex(0, src); tex(1, T.c); gl.uniform1i(u.u_tex, 0); gl.uniform1i(u.u_add, 1); gl.uniform1f(u.u_strength, num(e.strength, glow ? 0.8 : 0.6) * boost); }, dst); swap();
      } else if (e.type === 'shake') {
        // the camera kicks with the band (bass by default), in a direction that changes 12 times a second
        const amp = num(e.amount, 0.006) * level(e.band ?? 'bass');
        const step = Math.floor(tl * 12), dirA = hash(step, 5) * TAU;
        full(P.shift, (u) => {
          tex(0, src); gl.uniform1i(u.u_tex, 0);
          gl.uniform2f(u.u_off, Math.cos(dirA) * amp, Math.sin(dirA) * amp);
          gl.uniform1f(u.u_zoom, 1 + num(e.amount, 0.006) * 2.5);
        }, dst); swap();
      } else if (e.type === 'ripple') {
        const nm = images.get(e.src);
        if (!nm) continue;
        const tiles = Array.isArray(e.tiles) ? e.tiles : [1, 1];
        const amt = Array.isArray(e.amount) ? e.amount : [num(e.amount, 0.005), num(e.amount, 0.005)];
        // the samples drift apart on one circle and slide together on another, so the loop closes
        const R = num(e.drift, 0.002) * Math.SQRT2 * LOOP / TAU;
        const sl = Array.isArray(e.slide) ? e.slide : [0, 0];
        const Rs = Math.hypot(num(sl[0], 0), num(sl[1], 0)) * LOOP / TAU, sa = Math.atan2(num(sl[1], 0), num(sl[0], 0));
        const c1 = Math.cos(TAU * ph), s1 = Math.sin(TAU * ph);
        const dx = R * (c1 - 1), dy = R * s1;
        const sx = Rs * (Math.cos(sa) * s1 - Math.sin(sa) * (1 - c1)), sy = Rs * (Math.sin(sa) * s1 + Math.cos(sa) * (1 - c1));
        const boost = e.band != null ? 1 + num(e.bandAmount, 1) * level(e.band) : 1;
        full(P.ripple, (u) => {
          tex(0, src); gl.uniform1i(u.u_tex, 0);
          tex(1, nm);
          gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.REPEAT);
          gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.REPEAT);
          gl.uniform1i(u.u_normal, 1);
          gl.uniform2f(u.u_tiles, num(tiles[0], 1), num(tiles[1], 1));
          gl.uniform4f(u.u_drift, dx + sx, dy + sy, -dx + sx, -dy + sy);
          gl.uniform2f(u.u_amount, num(amt[0], 0.005) * boost, num(amt[1], 0.005) * boost);
        }, dst); swap();
      } else if (e.type === 'lens') {
        const fr = Array.isArray(e.frame) ? e.frame : [CW, CH];
        const fa = num(fr[0], CW) / Math.max(1, num(fr[1], CH)), sa = CW / CH;
        // the frame covers the screen: the screen shows all of its narrower side
        const fscale = fa > sa ? [sa / fa, 1] : [1, fa / sa];
        full(P.lens, (u) => {
          tex(0, src); gl.uniform1i(u.u_tex, 0);
          gl.uniform1f(u.u_amount, num(e.amount, -0.05));
          gl.uniform2f(u.u_fscale, fscale[0], fscale[1]);
          gl.uniform1f(u.u_faspect, fa);
        }, dst); swap();
      } else if (e.type === 'chromatic') {
        const amt = num(e.amount, 0.002) + (e.band != null ? num(e.bandAmount, 0.01) * level(e.band) : 0);
        full(P.chroma, (u) => { tex(0, src); gl.uniform1i(u.u_tex, 0); gl.uniform1f(u.u_amount, amt); }, dst); swap();
      }
    }
    // live: onto the screen; rendering: the host reads the last target straight back
    if (MODE === 'live') full(P.copy, (u) => { tex(0, src); gl.uniform1i(u.u_tex, 0); }, null);
    return src;
  }

  // ------------------------------------------------------------------ live: the frame clock, the mouse, the sound
  function startLive() {
    let paused = false, start = -1, raf = 0;
    const resize = () => {
      const dpr = window.devicePixelRatio || 1;
      canvas.width = Math.max(1, Math.round(window.innerWidth * dpr));
      canvas.height = Math.max(1, Math.round(window.innerHeight * dpr));
    };
    resize();
    window.addEventListener('resize', resize);
    window.addEventListener('mousemove', (e) => {
      pointer.tx = clamp(e.clientX / Math.max(1, window.innerWidth) * 2 - 1, -1, 1);
      pointer.ty = clamp(e.clientY / Math.max(1, window.innerHeight) * 2 - 1, -1, 1);
      pointer.seen = true;
    });
    if (STILL !== null) {
      pointer.x = pointer.tx = clamp(+params.get('mx') || 0, -1, 1);
      pointer.y = pointer.ty = clamp(+params.get('my') || 0, -1, 1);
      pointer.seen = params.has('mx');
      const once = () => { try { draw(STILL); } catch (e) { post({ type: 'error', message: String(e) }); } };
      once();
      window.addEventListener('resize', () => requestAnimationFrame(once));
      return;
    }
    const frame = (ts) => {
      raf = 0;
      if (paused) return;
      if (start < 0) start = ts;
      const dt = lastDraw ? Math.min(0.1, (ts - lastDraw) / 1000) : 0;
      lastDraw = ts;
      pointer.x += (pointer.tx - pointer.x) * Math.min(1, dt * 5);
      pointer.y += (pointer.ty - pointer.y) * Math.min(1, dt * 5);
      try { draw((ts - start) / 1000); } catch (e) { post({ type: 'error', message: String(e) }); return; }
      raf = requestAnimationFrame(frame);
    };
    raf = requestAnimationFrame(frame);
    // Wallpaper Engine's API, as the host's shim provides it: pause, and the sound bands
    window.wallpaperPropertyListener = {
      setPaused(p) { paused = !!p; if (!paused && !raf) { lastDraw = 0; raf = requestAnimationFrame(frame); } },
      applyGeneralProperties() {},
      applyUserProperties() {},
    };
    if (realtime && !FAKE_AUDIO && window.wallpaperRegisterAudioListener) {
      window.wallpaperRegisterAudioListener((a) => {
        if (!a || a.length < 128) return;
        // each band rises at once and falls back gently, so bars and pulses do not flicker
        for (let i = 0; i < 64; i++) {
          const val = clamp((a[i] + a[i + 64]) / 2, 0, 1.5);
          spectrum[i] = val > spectrum[i] ? val : spectrum[i] * 0.8 + val * 0.2;
        }
        summarize();
      });
    }
  }

  // ------------------------------------------------------------------ render: the host asks for each frame by its time
  function startRender() {
    canvas.width = RENDER_W; canvas.height = RENDER_H;
    let view = null;
    host.addEventListener('sharedbufferreceived', (e) => {
      view = new Uint8Array(e.getBuffer());
      post({ type: 'buffer-ok', bytes: view.length });
    });
    host.addEventListener('message', (e) => {
      const m = e.data;
      if (!m || m.type !== 'frame') return;
      try {
        if (!view) throw new Error('sem o buffer do quadro');
        // the final target, rows bottom-up (the host flips them)
        bindTarget(draw(m.t));
        gl.readPixels(0, 0, RENDER_W, RENDER_H, gl.RGBA, gl.UNSIGNED_BYTE, view);
        post({ type: 'frame-done', i: m.i });
      } catch (err) {
        post({ type: 'error', message: String(err) });
      }
    });
  }

  (async () => {
    try {
      buildPrograms();
      await load();
      if (MODE === 'render') startRender(); else startLive();
      post({ type: 'vivo-ready', realtime, loop: LOOP, warnings, width: canvas.width, height: canvas.height });
    } catch (e) {
      post({ type: 'error', message: String(e && e.message || e) });
    }
  })();
})();
