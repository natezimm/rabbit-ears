// The tube-TV picture for mpv: a gentle barrel bulge, rounded corners, a
// vignette toward the edges and faint scanlines. The same maths as the TV
// page's WebGL shader (web/src/app/tv/crt-shader.ts), so both screens look
// alike. The box writes out a copy with its Box:Picture:Crt settings for mpv
// to load.

//!HOOK OUTPUT
//!BIND HOOKED
//!DESC Rabbit Ears tube

// Replaced with the box's own settings when it writes this file out for mpv.
#define CURVATURE 0.12
#define CORNER_RADIUS 0.065
#define VIGNETTE 0.25
#define SCANLINES 0.12

#define PI 3.14159265
#define SCAN_LINES 480.0

vec4 hook() {
  vec2 uv = HOOKED_pos;
  vec2 cc = uv - 0.5;
  float dist2 = dot(cc, cc);

  // Barrel distortion: push pixels outward with distance from centre.
  vec2 warped = uv + cc * dist2 * CURVATURE;

  // Rounded-rectangle mask over the warped frame, anti-aliased at the edge.
  vec2 d = abs(warped - 0.5) - (vec2(0.5) - CORNER_RADIUS);
  float outside = length(max(d, vec2(0.0))) - CORNER_RADIUS;
  float aa = 1.5 / max(HOOKED_size.x, HOOKED_size.y);
  float mask = 1.0 - smoothstep(0.0, aa, outside);
  if (mask <= 0.0) return vec4(0.0, 0.0, 0.0, 1.0);

  vec3 col = HOOKED_tex(clamp(warped, 0.0, 1.0)).rgb;

  // Vignette: darken toward the edges and corners.
  col *= clamp(1.0 - VIGNETTE * dist2 * 4.0, 0.0, 1.0);

  // Scanlines: subtle horizontal darkening at a standard-definition line pitch.
  col *= 1.0 - SCANLINES * (0.5 + 0.5 * cos(warped.y * SCAN_LINES * PI));

  return vec4(col * mask, 1.0);
}
