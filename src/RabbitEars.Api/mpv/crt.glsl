// The tube-TV picture for mpv: a gentle barrel bulge, rounded corners, a
// vignette toward the edges and faint scanlines. The same maths as the TV
// page's WebGL shader (web/src/app/tv/crt-shader.ts), so both screens look
// alike. The box writes out a copy with its Box:Picture:Crt settings for mpv
// to load.

//!HOOK OUTPUT
//!BIND HOOKED
//!DESC Rabbit Ears tube

// Replaced with the box's own settings when it writes this file out for mpv.
#define CURVATURE 0.15
#define CORNER_RADIUS 0.05
#define VIGNETTE 0.3
#define SCANLINES 0.1

#define SCAN_LINES 480.0

vec4 hook() {
  // Centred tube coordinates: -1..1 across the picture, y down.
  vec2 p = HOOKED_pos * 2.0 - 1.0;

  // Bulge: each axis bends by how far out the other one is, so the edges bow
  // outward like glass over a tube and the corners stretch the most.
  vec2 bent = p * (1.0 + 0.5 * CURVATURE * p.yx * p.yx);

  // Rounded corners, measured in pixels so they stay round on any shape of
  // screen, with an edge that fades over one pixel.
  vec2 halfSize = 0.5 * HOOKED_size;
  float radius = CORNER_RADIUS * min(HOOKED_size.x, HOOKED_size.y);
  vec2 inset = abs(bent) * halfSize - (halfSize - radius);
  float edge = length(max(inset, 0.0)) + min(max(inset.x, inset.y), 0.0) - radius;
  float mask = clamp(0.5 - edge, 0.0, 1.0);
  if (mask <= 0.0) return vec4(0.0, 0.0, 0.0, 1.0);

  vec2 warped = bent * 0.5 + 0.5;
  vec3 col = HOOKED_tex(clamp(warped, 0.0, 1.0)).rgb;

  // Vignette: the picture dims toward the edges of the glass, most in the corners.
  vec2 centred = warped * (1.0 - warped) * 4.0;
  col *= pow(clamp(centred.x * centred.y, 1e-4, 1.0), VIGNETTE * 0.6);

  // Scanlines: each line is a soft beam, brightest along its middle, at a
  // standard-definition line count.
  float row = fract(warped.y * SCAN_LINES) - 0.5;
  col *= 1.0 - SCANLINES * 1.2 * (1.0 - exp(-8.0 * row * row));

  return vec4(col * mask, 1.0);
}
