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

  const P = {};
  function buildPrograms() {
    P.image = program(QUAD_VS, HEAD + `in vec2 v_uv; uniform sampler2D u_tex; uniform vec4 u_uv; uniform vec4 u_tint; uniform float u_opacity; out vec4 o;
      void main() { vec4 c = texture(u_tex, u_uv.xy + v_uv * u_uv.zw); o = vec4(c.rgb * u_tint.rgb, c.a) * u_opacity * u_tint.a; }`);

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

    // Particles: every position is a closed-form function of the loop phase and the particle's seeded randoms,
    // with whole numbers of wraps and sways per loop, so the last frame of the loop equals the first.
    P.particles = program(HEAD + `in vec2 a; uniform vec2 u_res; uniform float u_s; uniform float u_ph; uniform float u_L;
      uniform int u_preset; uniform float u_seed; uniform float u_size; uniform float u_speed; uniform float u_angle;
      uniform vec2 u_offset; out vec2 v_q; out float v_alpha; flat out int v_preset;
      float h(float n) { return fract(sin(n * 12.9898 + u_seed * 78.233) * 43758.5453); }
      float wraps(float seconds) { return max(1.0, floor(u_L / seconds + 0.5)); }
      void main() {
        float id = float(gl_InstanceID);
        float r1 = h(id * 7.1 + 1.0), r2 = h(id * 7.1 + 2.0), r3 = h(id * 7.1 + 3.0), r4 = h(id * 7.1 + 4.0), r5 = h(id * 7.1 + 5.0), r6 = h(id * 7.1 + 6.0);
        vec2 c; vec2 size; float alpha; float rot = 0.0;
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
        } else {                        // fireflies: wander in the lower part and blink
          float m1 = wraps(6.0 + 5.0 * r3), m2 = wraps(8.0 + 5.0 * r4), m3 = wraps(2.0 + 3.0 * r5), m4 = wraps(3.0 + 2.0 * r6);
          c = vec2(r2 * u_res.x + 0.06 * u_s * sin(6.2831853 * (m1 * u_ph + r3)) + 0.02 * u_s * sin(6.2831853 * (m4 * u_ph + r6)),
                   (0.35 + 0.6 * r1) * u_res.y + 0.05 * u_s * cos(6.2831853 * (m2 * u_ph + r4)));
          size = vec2(u_size * u_s * (0.7 + 0.6 * r6)) * 6.0;
          alpha = pow(max(0.0, sin(6.2831853 * (m3 * u_ph + r5))), 3.0);
        }
        vec2 local = (a - 0.5) * size;
        local = vec2(cos(rot) * local.x - sin(rot) * local.y, sin(rot) * local.x + cos(rot) * local.y);
        vec2 p = c + local + u_offset;
        v_q = a - 0.5; v_alpha = alpha; v_preset = u_preset;
        gl_Position = vec4(p.x / u_res.x * 2.0 - 1.0, 1.0 - p.y / u_res.y * 2.0, 0.0, 1.0);
      }`, HEAD + `in vec2 v_q; in float v_alpha; flat in int v_preset; uniform vec4 u_color; uniform float u_opacity; out vec4 o;
      void main() {
        float a;
        if (v_preset == 0) a = exp(-pow(v_q.x * 4.0, 2.0)) * smoothstep(0.5, 0.0, abs(v_q.y)) ;
        else if (v_preset == 3) { float r = length(v_q) * 2.0; a = exp(-r * r * 18.0) + 0.35 * exp(-r * r * 3.5); }
        else { float r = length(v_q) * 2.0; a = smoothstep(1.0, 0.25, r); }
        a *= v_alpha * u_color.a * u_opacity;
        o = vec4(u_color.rgb * a, a);
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
    P.grain = program(FULL_VS, HEAD + `in vec2 v_uv; uniform sampler2D u_tex; uniform float u_amount; uniform float u_frame; uniform vec2 u_res; out vec4 o;
      float h(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233)) + u_frame * 0.618) * 43758.5453); }
      void main() { vec3 c = texture(u_tex, v_uv).rgb; float n = h(floor(v_uv * u_res)) - 0.5; o = vec4(clamp(c + n * u_amount, 0.0, 1.0), 1.0); }`);
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
  }

  // ------------------------------------------------------------------ geometry and render targets
  const quad = gl.createBuffer();
  gl.bindBuffer(gl.ARRAY_BUFFER, quad);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([0, 0, 1, 0, 0, 1, 0, 1, 1, 0, 1, 1]), gl.STATIC_DRAW);
  const vao = gl.createVertexArray();
  gl.bindVertexArray(vao);
  gl.enableVertexAttribArray(0);
  gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);

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

  // ------------------------------------------------------------------ the wallpaper
  let W = null;        // parsed wallpaper
  let LOOP = 20;
  const images = new Map();
  const audio = { bass: 0, mid: 0, treble: 0, all: 0 };
  let realtime = false;
  const ANCHORS = {
    'center': [0.5, 0.5], 'top': [0.5, 0], 'bottom': [0.5, 1], 'left': [0, 0.5], 'right': [1, 0.5],
    'top-left': [0, 0], 'top-right': [1, 0], 'bottom-left': [0, 1], 'bottom-right': [1, 1],
  };
  const PARTICLES = { rain: [0, 900], snow: [1, 380], dust: [2, 160], fireflies: [3, 60] };

  function snapPeriod(p, where) {
    p = num(p, LOOP);
    const n = Math.max(1, Math.round(LOOP / p));
    const snapped = LOOP / n;
    if (Math.abs(snapped - p) > 1e-3) warn(`${where}: período ${p} não divide o loop de ${LOOP} s; usando ${snapped.toFixed(3)}`);
    return snapped;
  }

  async function load() {
    const res = await fetch('wallpaper.json', { cache: 'no-store' });
    if (!res.ok) throw new Error('wallpaper.json não encontrado');
    W = await res.json();
    if (W.format !== 'fundo-vivo') throw new Error('não é um wallpaper do Fundo Vivo');
    if (W.version !== 1) warn(`versão ${W.version} do formato; este runtime conhece a 1`);
    LOOP = clamp(num(W.loop, 20), 1, 120);
    W.layers = Array.isArray(W.layers) ? W.layers : [];
    W.effects = Array.isArray(W.effects) ? W.effects : [];
    W.layers.forEach((l, i) => {
      l.animate = Array.isArray(l.animate) ? l.animate : [];
      l.animate.forEach((a, j) => {
        const where = `camada ${l.name || i + 1}, animação ${j + 1}`;
        if (a.type === 'audio') realtime = true;
        else a._period = snapPeriod(a.period, where);
        if (a.type === 'spin') {
          // whole turns per loop, or the last frame would not match the first
          const perLoop = num(a.amount, 1) * LOOP / a._period, whole = Math.round(perLoop) || Math.sign(perLoop) || 1;
          if (Math.abs(whole - perLoop) > 1e-3) warn(`${where}: ${perLoop.toFixed(2)} voltas por loop; usando ${whole}`);
          a._turns = whole;
        }
        a._phases = [1, 2, 3].map((k) => hash(i, j, k));
      });
    });
    await Promise.all(W.layers.filter((l) => l.type === 'image' && l.src).map(async (l) => {
      if (images.has(l.src)) return;
      const img = new Image();
      img.src = l.src;
      try { await img.decode(); } catch (e) { warn(`imagem ${l.src} não abriu`); return; }
      const t = gl.createTexture();
      gl.bindTexture(gl.TEXTURE_2D, t);
      gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, true);
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, img);
      gl.generateMipmap(gl.TEXTURE_2D);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
      images.set(l.src, { tex: t, w: img.naturalWidth, h: img.naturalHeight });
    }));
  }

  // property values of a layer at loop time tl (seconds)
  function animated(l, tl) {
    const v = { x: num(l.x, 0), y: num(l.y, 0), scale: num(l.scale, 1), rotation: num(l.rotation, 0), opacity: num(l.opacity, 1) };
    for (const a of l.animate) {
      if (!(a.property in v)) continue;
      const amount = num(a.amount, a.type === 'spin' ? 1 : 0);
      let d = 0;
      if (a.type === 'oscillate') d = amount * Math.sin(TAU * (tl / a._period + num(a.phase, 0)));
      else if (a.type === 'spin') d = 360 * (a._turns * tl / LOOP + num(a.phase, 0));
      else if (a.type === 'noise') {
        // smooth noise that repeats every period: a few harmonics with seeded phases
        const x = tl / a._period + num(a.phase, 0);
        d = amount * (Math.sin(TAU * (x + a._phases[0])) * 0.55 + Math.sin(TAU * (2 * x + a._phases[1])) * 0.3 + Math.sin(TAU * (3 * x + a._phases[2])) * 0.15);
      } else if (a.type === 'audio') d = amount * (audio[a.band] ?? audio.all);
      v[a.property] += d;
    }
    v.opacity = clamp(v.opacity, 0, 1);
    return v;
  }

  // ------------------------------------------------------------------ drawing one frame
  const pointer = { x: 0, y: 0, tx: 0, ty: 0 };
  let lastDraw = 0;

  function draw(t) {
    const CW = canvas.width, CH = canvas.height;
    ensureTargets(CW, CH);
    const tl = ((t % LOOP) + LOOP) % LOOP, ph = tl / LOOP;
    // layout box: the monitor's part of the canvas (smaller than the canvas only when rendering with a pan margin)
    const BW = CW / (1 + 2 * MARGIN), BH = CH / (1 + 2 * MARGIN), BX = (CW - BW) / 2, BY = (CH - BH) / 2;
    const S = Math.min(BW, BH);
    const par = MODE === 'live' ? num(W.parallax, 0) : 0;

    bindTarget(T.a);
    const bg = color(W.background, [0, 0, 0, 1]);
    gl.clearColor(bg[0], bg[1], bg[2], 1);
    gl.clear(gl.COLOR_BUFFER_BIT);

    W.layers.forEach((l, i) => {
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
        gl.uniform1i(P.image.u.u_tex, 0);
        gl.uniform4fv(P.image.u.u_tint, color(l.tint, [1, 1, 1, 1]));
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
      } else if (l.type === 'shape') {
        const kind = l.shape === 'ring' ? 1 : l.shape === 'rect' ? 2 : 0;
        const w = num(l.size, 0.1) * S * v.scale, h = kind === 2 ? num(l.height, num(l.size, 0.1)) * S * v.scale : w;
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
        const count = Math.min(20000, Math.round(preset[1] * Math.max(0, num(l.density, 1)) * (CW * CH) / (1920 * 1080)));
        if (count <= 0) return;
        if (l.preset === 'fireflies' && !l.blend) setBlend('add');
        gl.useProgram(P.particles.p);
        const u = P.particles.u;
        gl.uniform2f(u.u_res, CW, CH);
        gl.uniform1f(u.u_s, S);
        gl.uniform1f(u.u_ph, ph);
        gl.uniform1f(u.u_L, LOOP);
        gl.uniform1i(u.u_preset, preset[0]);
        gl.uniform1f(u.u_seed, num(l.seed, i + 1));
        gl.uniform1f(u.u_size, num(l.size, l.preset === 'rain' ? 0.0015 : l.preset === 'snow' ? 0.006 : 0.004) * v.scale);
        gl.uniform1f(u.u_speed, Math.max(0.05, num(l.speed, 1)));
        gl.uniform1f(u.u_angle, num(l.angle, 8));
        gl.uniform2f(u.u_offset, offX, offY);
        gl.uniform4fv(u.u_color, color(l.color, l.preset === 'fireflies' ? [1, 0.85, 0.4, 1] : [1, 1, 1, 1]));
        gl.uniform1f(u.u_opacity, v.opacity);
        gl.drawArraysInstanced(gl.TRIANGLES, 0, 6, count);
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
        full(P.grain, (u) => { tex(0, src); gl.uniform1i(u.u_tex, 0); gl.uniform1f(u.u_amount, num(e.amount, 0.05)); gl.uniform1f(u.u_frame, frame); gl.uniform2f(u.u_res, CW, CH); }, dst); swap();
      } else if (e.type === 'glow' || e.type === 'rays') {
        const glow = e.type === 'glow';
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
        full(P.add, (u) => { tex(0, src); tex(1, T.c); gl.uniform1i(u.u_tex, 0); gl.uniform1i(u.u_add, 1); gl.uniform1f(u.u_strength, num(e.strength, glow ? 0.8 : 0.6)); }, dst); swap();
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
    });
    if (STILL !== null) {
      pointer.x = clamp(+params.get('mx') || 0, -1, 1);
      pointer.y = clamp(+params.get('my') || 0, -1, 1);
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
    if (realtime && window.wallpaperRegisterAudioListener) {
      window.wallpaperRegisterAudioListener((a) => {
        if (!a || a.length < 128) return;
        const avg = (from, to) => { let s = 0; for (let i = from; i < to; i++) s += a[i] + a[i + 64]; return s / ((to - from) * 2); };
        const ease = (k, v) => { audio[k] = v > audio[k] ? v : audio[k] * 0.8 + v * 0.2; };
        ease('bass', avg(0, 8)); ease('mid', avg(8, 32)); ease('treble', avg(32, 64)); ease('all', avg(0, 64));
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
