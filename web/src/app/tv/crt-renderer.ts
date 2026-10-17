import { CrtConfig } from '../core/models';
import { FRAGMENT_SHADER, VERTEX_SHADER } from './crt-shader';

export type ScreenMode = 'video' | 'snow' | 'glitch' | 'bars' | 'blank';

const MODE_INDEX: Record<ScreenMode, number> = { video: 0, snow: 1, glitch: 2, bars: 3, blank: 4 };
const UNIFORMS = [
  'uFrame', 'uTube', 'uTubePixels', 'uCurvature', 'uCornerRadius',
  'uVignette', 'uScanlines', 'uMode', 'uTime', 'uPower',
] as const;

/** Old shows were made for 4:3 tubes; assume that until a video says otherwise. */
const DEFAULT_ASPECT = 4 / 3;
/** How long without a frame callback before falling back to watching the clock. */
const FRAME_CALLBACK_QUIET_MS = 250;
/** Keeps shader time small enough that the noise does not lose precision after days of uptime. */
const TIME_WRAP_SECONDS = 600;

/**
 * Draws the picture through the CRT shader onto a full-screen canvas. Video
 * frames are copied into a texture as they arrive; the shader bends them.
 */
export class CrtRenderer {
  private readonly uniforms = {} as Record<(typeof UNIFORMS)[number], WebGLUniformLocation | null>;
  private source: HTMLVideoElement | null = null;
  private frameWaiting = false;
  private lastFrameCallback = 0;
  private copiedTime = -1;
  private hasFrame = false;
  private aspect = DEFAULT_ASPECT;

  /** Returns null when the browser has no usable WebGL, so the caller can fall back to plain video. */
  static create(canvas: HTMLCanvasElement, crt: CrtConfig): CrtRenderer | null {
    const attributes: WebGLContextAttributes = { alpha: false, antialias: false, powerPreference: 'high-performance' };
    const gl = (canvas.getContext('webgl2', attributes) ?? canvas.getContext('webgl', attributes)) as
      | WebGLRenderingContext
      | null;
    if (!gl) return null;
    try {
      return new CrtRenderer(canvas, gl, crt);
    } catch (error) {
      console.error('CRT shader unavailable; falling back to plain video.', error);
      return null;
    }
  }

  private constructor(
    private readonly canvas: HTMLCanvasElement,
    private readonly gl: WebGLRenderingContext,
    private crt: CrtConfig,
  ) {
    const program = this.link(VERTEX_SHADER, FRAGMENT_SHADER);
    gl.useProgram(program);
    for (const name of UNIFORMS) this.uniforms[name] = gl.getUniformLocation(program, name);

    // One triangle that covers the whole screen.
    gl.bindBuffer(gl.ARRAY_BUFFER, gl.createBuffer());
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);
    const position = gl.getAttribLocation(program, 'aPosition');
    gl.enableVertexAttribArray(position);
    gl.vertexAttribPointer(position, 2, gl.FLOAT, false, 0, 0);

    gl.bindTexture(gl.TEXTURE_2D, gl.createTexture());
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.uniform1i(this.uniforms.uFrame, 0);
  }

  configure(crt: CrtConfig): void {
    this.crt = crt;
  }

  /**
   * Draw one frame. With mode 'video' and no element, the last frame that was
   * copied stays on the glass, which is what lets the picture collapse on its
   * way to standby after the player has already stopped.
   */
  draw(mode: ScreenMode, video: HTMLVideoElement | null, power: number, seconds: number): void {
    const { gl, canvas, uniforms, crt } = this;
    this.resize();

    if (mode === 'video') {
      if (video) this.copyFrame(video);
      if (!this.hasFrame) mode = 'blank';
    }

    // Fit the picture inside the screen at its own shape.
    const screenAspect = canvas.width / canvas.height;
    const width = Math.min(1, this.aspect / screenAspect);
    const height = Math.min(1, screenAspect / this.aspect);
    gl.uniform4f(uniforms.uTube, (1 - width) / 2, (1 - height) / 2, width, height);
    gl.uniform2f(uniforms.uTubePixels, width * canvas.width, height * canvas.height);

    gl.uniform1f(uniforms.uCurvature, crt.enabled ? crt.curvature : 0);
    gl.uniform1f(uniforms.uCornerRadius, crt.enabled ? crt.cornerRadius : 0);
    gl.uniform1f(uniforms.uVignette, crt.enabled ? crt.vignette : 0);
    gl.uniform1f(uniforms.uScanlines, crt.enabled && crt.scanlines ? crt.scanlineIntensity : 0);
    gl.uniform1i(uniforms.uMode, MODE_INDEX[mode]);
    gl.uniform1f(uniforms.uTime, seconds % TIME_WRAP_SECONDS);
    gl.uniform1f(uniforms.uPower, power);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
  }

  private copyFrame(video: HTMLVideoElement): void {
    if (video !== this.source) this.watch(video);
    if (video.readyState < HTMLMediaElement.HAVE_CURRENT_DATA || !video.videoWidth) return;

    // Frame callbacks say exactly when a new frame is up. Where they are
    // missing or have gone quiet, notice instead that playback has moved on.
    const callbacksQuiet = performance.now() - this.lastFrameCallback > FRAME_CALLBACK_QUIET_MS;
    const movedOn = callbacksQuiet && video.currentTime !== this.copiedTime;
    if (!this.frameWaiting && !movedOn) return;

    const { gl } = this;
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, video);
    this.aspect = video.videoWidth / video.videoHeight;
    this.copiedTime = video.currentTime;
    this.hasFrame = true;
    this.frameWaiting = false;
  }

  /** Follow a different video element. */
  private watch(video: HTMLVideoElement): void {
    this.source = video;
    this.hasFrame = false;
    this.frameWaiting = true;
    this.lastFrameCallback = 0;
    if (!('requestVideoFrameCallback' in video)) return;

    const onFrame = () => {
      if (this.source !== video) return;
      this.frameWaiting = true;
      this.lastFrameCallback = performance.now();
      video.requestVideoFrameCallback(onFrame);
    };
    video.requestVideoFrameCallback(onFrame);
  }

  private resize(): void {
    const { canvas, gl } = this;
    const scale = window.devicePixelRatio || 1;
    const width = Math.round(canvas.clientWidth * scale);
    const height = Math.round(canvas.clientHeight * scale);
    if (!width || !height || (canvas.width === width && canvas.height === height)) return;
    canvas.width = width;
    canvas.height = height;
    gl.viewport(0, 0, width, height);
  }

  private link(vertexSource: string, fragmentSource: string): WebGLProgram {
    const { gl } = this;
    const program = gl.createProgram();
    for (const [type, source] of [[gl.VERTEX_SHADER, vertexSource], [gl.FRAGMENT_SHADER, fragmentSource]] as const) {
      const shader = gl.createShader(type)!;
      gl.shaderSource(shader, source);
      gl.compileShader(shader);
      if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
        throw new Error(gl.getShaderInfoLog(shader) ?? 'shader failed to compile');
      }
      gl.attachShader(program, shader);
    }
    gl.linkProgram(program);
    if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
      throw new Error(gl.getProgramInfoLog(program) ?? 'shader failed to link');
    }
    return program;
  }
}
