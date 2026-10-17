// The tube-TV picture, as a WebGL fragment shader: a gentle barrel bulge,
// rounded corners, a vignette toward the edges and faint scanlines. Snow,
// glitch blocks and colour bars are drawn here too rather than played from
// clips, which keeps them inside the curved glass like everything else.
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
  // Centred tube coordinates: -1..1 across the picture, y down.
  vec2 p = (vUv - uTube.xy) / uTube.zw * 2.0 - 1.0;

  // Bulge: each axis bends by how far out the other one is, so the edges bow
  // outward like glass over a tube and the corners stretch the most.
  vec2 bent = p * (1.0 + 0.5 * uCurvature * p.yx * p.yx);

  // Rounded corners, measured in pixels so they stay round on any shape of
  // screen, with an edge that fades over one pixel.
  vec2 halfSize = 0.5 * uTubePixels;
  float radius = uCornerRadius * min(uTubePixels.x, uTubePixels.y);
  vec2 inset = abs(bent) * halfSize - (halfSize - radius);
  float edge = length(max(inset, 0.0)) + min(max(inset.x, inset.y), 0.0) - radius;
  float mask = clamp(0.5 - edge, 0.0, 1.0);
  if (mask <= 0.0) {
    gl_FragColor = vec4(0.0, 0.0, 0.0, 1.0);
    return;
  }

  vec2 warped = bent * 0.5 + 0.5;

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

  // Vignette: the picture dims toward the edges of the glass, most in the corners.
  vec2 centred = warped * (1.0 - warped) * 4.0;
  col *= pow(clamp(centred.x * centred.y, 1e-4, 1.0), uVignette * 0.6);

  // Scanlines: each line is a soft beam, brightest along its middle, at a
  // standard-definition line count.
  float row = fract(warped.y * SCAN_LINES) - 0.5;
  col *= 1.0 - uScanlines * 1.2 * (1.0 - exp(-8.0 * row * row));

  gl_FragColor = vec4(col * mask, 1.0);
}
`;
