// The tube-TV picture, as a WebGL fragment shader: a gentle barrel bulge,
// rounded corners, a vignette toward the edges and faint scanlines. Snow,
// glitch blocks and colour bars are drawn here too rather than played from
// clips, which keeps them inside the curved glass like everything else.
//
// The curvature, corner mask, vignette and scanline maths are adapted from
// NostalgiaBox (MIT); see THIRD-PARTY-NOTICES.md.
//
// Written in GLSL ES 1.00 so it runs on both WebGL 1 and WebGL 2.

export const VERTEX_SHADER = `
attribute vec2 aPosition;
varying vec2 vUv;

void main() {
  // Screen UV with y pointing down, matching video frames and CSS.
  vUv = vec2(aPosition.x * 0.5 + 0.5, 0.5 - aPosition.y * 0.5);
  gl_Position = vec4(aPosition, 0.0, 1.0);
}
`;

export const FRAGMENT_SHADER = `
precision highp float;

varying vec2 vUv;

uniform sampler2D uFrame;
uniform vec4 uTube;         // the picture's rectangle in screen UV: x, y, width, height
uniform vec2 uTubePixels;   // the same rectangle's size in device pixels
uniform float uCurvature;
uniform float uCornerRadius;
uniform float uVignette;
uniform float uScanlines;   // intensity; 0 turns them off
uniform int uMode;          // 0 video, 1 snow, 2 glitch, 3 colour bars, 4 blank
uniform float uTime;        // seconds, wrapped by the caller to stay precise
uniform float uPower;       // 1 on, 0 off; in between the picture collapses

const float PI = 3.14159265;
const float SCAN_LINES = 480.0;

// Dave Hoskins' hash12: even-looking noise with no visible lattice.
float hash(vec2 p) {
  vec3 p3 = fract(vec3(p.xyx) * 0.1031);
  p3 += dot(p3, p3.yzx + 33.33);
  return fract((p3.x + p3.y) * p3.z);
}

// Analog snow: fine grey grain that changes every frame, with a faint band
// rolling up the screen.
vec3 snow(vec2 uv) {
  float frame = floor(uTime * 30.0);
  vec2 grain = floor(uv * vec2(360.0, 270.0));
  float n = hash(grain + frame * vec2(37.0, 17.0));
  float band = 0.07 * sin((uv.y + uTime * 0.6) * 14.0);
  return vec3(clamp(n * 0.9 + band, 0.0, 1.0));
}

// Chunky coloured blocks, like corrupted digital video.
vec3 glitch(vec2 uv) {
  float frame = floor(uTime * 25.0);
  vec2 block = floor(uv * vec2(96.0, 54.0)) + frame * vec2(31.0, 73.0);
  return vec3(hash(block), hash(block + 19.19), hash(block + 73.7));
}

vec3 barColour(float v) {
  // v counts down 7..1 across the screen: bit 2 is green, bit 1 red, bit 0 blue.
  return 0.75 * vec3(mod(floor(v / 2.0), 2.0), floor(v / 4.0), mod(v, 2.0));
}

// The "no signal" test card.
vec3 bars(vec2 uv) {
  float column = floor(clamp(uv.x, 0.0, 0.999) * 7.0);
  if (uv.y < 0.67) return barColour(7.0 - column);
  if (uv.y < 0.75) {
    return mod(column, 2.0) < 0.5 ? barColour(column + 1.0) : vec3(0.075);
  }
  float block = floor(clamp(uv.x, 0.0, 0.999) * 6.0);
  if (block < 1.0) return vec3(0.0, 0.13, 0.3);
  if (block < 2.0) return vec3(1.0);
  if (block < 3.0) return vec3(0.2, 0.0, 0.42);
  return vec3(0.075);
}

void main() {
  vec2 uv = (vUv - uTube.xy) / uTube.zw;
  vec2 cc = uv - 0.5;
  float dist2 = dot(cc, cc);

  // Barrel distortion: push pixels outward with distance from centre.
  vec2 warped = uv + cc * dist2 * uCurvature;

  // Rounded-rectangle mask over the warped frame, anti-aliased at the edge.
  vec2 d = abs(warped - 0.5) - (vec2(0.5) - uCornerRadius);
  float outside = length(max(d, vec2(0.0))) - uCornerRadius;
  float aa = 1.5 / max(uTubePixels.x, uTubePixels.y);
  float mask = 1.0 - smoothstep(0.0, aa, outside);
  if (mask <= 0.0) {
    gl_FragColor = vec4(0.0, 0.0, 0.0, 1.0);
    return;
  }

  // Switching off: the picture squeezes to a bright line, then to a dot.
  float tall = clamp((uPower - 0.35) / 0.65, 0.0, 1.0);
  float wide = clamp(uPower / 0.35, 0.0, 1.0);
  vec2 squeeze = vec2(max(wide, 0.0001), max(tall * tall, 0.004));
  vec2 src = (warped - 0.5) / squeeze + 0.5;

  vec3 col = vec3(0.0);
  bool onGlass = uPower > 0.001 && src.x >= 0.0 && src.x <= 1.0 && src.y >= 0.0 && src.y <= 1.0;
  if (onGlass) {
    if (uMode == 0) col = texture2D(uFrame, clamp(src, 0.0, 1.0)).rgb;
    else if (uMode == 1) col = snow(src);
    else if (uMode == 2) col = glitch(src);
    else if (uMode == 3) col = bars(src);
    // The collapsing line burns bright, whatever was on.
    col = mix(col, vec3(1.0), (1.0 - tall) * 0.85);
  }

  // Vignette: darken toward the edges and corners.
  col *= clamp(1.0 - uVignette * dist2 * 4.0, 0.0, 1.0);

  // Scanlines: subtle horizontal darkening at a standard-definition line pitch.
  col *= 1.0 - uScanlines * (0.5 + 0.5 * cos(warped.y * SCAN_LINES * PI));

  gl_FragColor = vec4(col * mask, 1.0);
}
`;
