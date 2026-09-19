using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TANGERINE_ZIP.Tools.LightTool
{
    public sealed class TangerineLightOverlay : Form
    {
        private readonly Form _targetForm;
        private readonly SynchronizationContext _syncContext;
        private readonly System.Threading.Timer _renderDriver;

        private volatile bool _tickPosted;
        private volatile bool _running;

        private POINT _mouseScreenPosition;

        private bool _mouseInside;

        private float _presentationDelayMs = 0f;

        private int _targetFps = 120;

        private float _radius = 180f;

        private Color _lightColor = Color.White;

        private float _lightStrength = 0.18f;

        private float _edgeStrength = 1.0f;

        private float _edgeWidth = 2.0f;

        private bool _isMouseLight = true;

        private bool _effectSuspended;

        private bool _effectAnimating;

        private float _effectOpacity;

        private int _eDelay = 50;

        private int _eAnimationTime = 50;

        private bool _hasMouseSample;

        private POINT _lastMouseScreenPosition;

        private long _effectReadyTimestamp;

        private long _animationStartTimestamp;

        private CancellationTokenSource? _renderCancellation;

        private readonly object _renderCancellationLock =
            new object();

        private int _renderGeneration;

        private bool _renderBusy;

        private long _lastMouseTimestamp;

        private readonly Stopwatch _frameStopwatch =
            new Stopwatch();

        private double _minFrameMilliseconds =
            1000.0 / 120.0;

        private readonly int _processorCount =
            Math.Max(
                1,
                Environment.ProcessorCount);

        private bool _timerResolutionRaised;

        /*
         * ============================================================
         * Coordinate correction
         * ============================================================
         *
         * 记录：
         *
         *     实际 Screen
         *         -
         *     PointToScreen(PointToClient(Screen))
         *
         * 得到的实际误差。
         *
         * 如果系统坐标转换存在：
         *
         *     +8,+8
         *
         * 那么这里就会得到：
         *
         *     -8,-8
         *
         * 最终把这个误差补回到 Layered Window 的位置。
         */
        private int _coordinateCorrectionX;
        private int _coordinateCorrectionY;

        private int _lastCoordinateCorrectionX;
        private int _lastCoordinateCorrectionY;

        #region Win32 Constants

        private const int WS_EX_LAYERED =
            0x00080000;

        private const int WS_EX_TRANSPARENT =
            0x00000020;

        private const int WS_EX_TOOLWINDOW =
            0x00000080;

        private const int WS_EX_NOACTIVATE =
            0x08000000;

        private const int WM_NCHITTEST =
            0x0084;

        private const int WM_MOUSEACTIVATE =
            0x0021;

        private const int WM_PRINT =
            0x0317;

        private const uint PW_CLIENTONLY =
            0x00000001;

        private const int HTTRANSPARENT =
            -1;

        private const int MA_NOACTIVATE =
            3;

        private const int ULW_ALPHA =
            0x00000002;

        private const byte AC_SRC_OVER =
            0;

        private const byte AC_SRC_ALPHA =
            1;

        private const int PRF_CHECKVISIBLE =
            0x00000001;

        private const int PRF_NONCLIENT =
            0x00000002;

        private const int PRF_CLIENT =
            0x00000004;

        private const int PRF_ERASEBKGND =
            0x00000008;

        private const int PRF_CHILDREN =
            0x00000010;

        private const uint DIB_RGB_COLORS =
            0;

        #endregion

        #region Structures

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;

            public POINT(
                int x,
                int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;

            public SIZE(
                int width,
                int height)
            {
                cx = width;
                cy = height;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RGBQUAD
        {
            public byte Blue;
            public byte Green;
            public byte Red;
            public byte Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public RGBQUAD bmiColors;
        }

        #endregion

        #region Win32 Imports

        [DllImport(
            "user32.dll",
            SetLastError = true)]
        private static extern bool UpdateLayeredWindow(
            IntPtr hWnd,
            IntPtr hdcDst,
            ref POINT pptDst,
            ref SIZE psize,
            IntPtr hdcSrc,
            ref POINT pptSrc,
            int crKey,
            ref BLENDFUNCTION pblend,
            int dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(
            IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(
            IntPtr hWnd,
            IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr hWnd,
            int msg,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PrintWindow(
            IntPtr hWnd,
            IntPtr hdcBlt,
            uint nFlags);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(
            out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(
            IntPtr hWnd,
            ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(
            IntPtr hWnd,
            ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(
            IntPtr hWnd,
            out RECT lpRect);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(
            IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(
            IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(
            IntPtr hdc,
            IntPtr h);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(
            IntPtr ho);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(
            IntPtr hdc,
            ref BITMAPINFO pbmi,
            uint iUsage,
            out IntPtr ppvBits,
            IntPtr hSection,
            uint dwOffset);

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(
            uint uPeriod);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(
            uint uPeriod);

        #endregion

        #region Native Resources

        private IntPtr _sourceDc;

        private IntPtr _sourceBitmap;

        private IntPtr _sourceBits;

        private int _sourceWidth;

        private int _sourceHeight;

        private IntPtr _outputDc;

        private IntPtr _outputBitmap;

        private IntPtr _outputBits;

        private int _outputWidth;

        private int _outputHeight;

        #endregion

        #region Managed Buffers

        private byte[] _sourceBuffer =
            Array.Empty<byte>();

        private byte[] _outputBuffer =
            Array.Empty<byte>();

        private float[] _lumaBuffer =
            Array.Empty<float>();

        private float[] _lightLut =
            Array.Empty<float>();

        private int _lutRadius;

        #endregion

        #region Frame State

        /*
         * 这里保存的永远是 Target Client 坐标。
         *
         * 不是 screen 坐标。
         */
        private int _lastOffsetX;

        private int _lastOffsetY;

        private int _lastWidth;

        private int _lastHeight;

        private bool _hasValidFrame;

        private bool _captureDirty =
            true;

        #endregion

        public TangerineLightOverlay(
            Form targetForm)
        {
            _targetForm =
                targetForm
                ?? throw new ArgumentNullException(
                    nameof(targetForm));

            _syncContext =
                SynchronizationContext.Current
                ?? new WindowsFormsSynchronizationContext();

            FormBorderStyle =
                FormBorderStyle.None;

            ShowInTaskbar =
                false;

            StartPosition =
                FormStartPosition.Manual;

            TopMost =
                false;

            TabStop =
                false;

            _targetForm.Move +=
                TargetForm_Move;

            _targetForm.Resize +=
                TargetForm_Resize;

            _targetForm.Invalidated +=
                TargetForm_Invalidated;

            _targetForm.FormClosed +=
                TargetForm_FormClosed;

            GetCursorPos(
                out _mouseScreenPosition);

            CreateNativeResources();

            RebuildLightLut();

            RaiseTimerResolution();

            _frameStopwatch.Start();

            _running = true;

            /*
             * 每 1ms 只是负责投递 UI render request。
             *
             * 实际帧率由 TargetFps 控制。
             */
            _renderDriver =
                new System.Threading.Timer(
                    PostRenderTick,
                    null,
                    0,
                    1);
        }

        #region Properties

        [Browsable(false)]
        [DesignerSerializationVisibility(
            DesignerSerializationVisibility.Hidden)]
        public int TargetFps
        {
            get => _targetFps;

            set
            {
                _targetFps =
                value;

                _minFrameMilliseconds =
                    1000.0 /
                    _targetFps;
            }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(
            DesignerSerializationVisibility.Hidden)]
        public int disableWhenMouseSpeedGetTooFast
        {
            get => _disableWhenMouseSpeedGetTooFast;

            set =>
                _disableWhenMouseSpeedGetTooFast =
                    Math.Max(
                        0,
                        value);
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(
            DesignerSerializationVisibility.Hidden)]
        public int eDelay
        {
            get => _eDelay;

            set =>
                _eDelay =
                    Math.Max(
                        0,
                        value);
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(
            DesignerSerializationVisibility.Hidden)]
        public int eAnimationTime
        {
            get => _eAnimationTime;

            set =>
                _eAnimationTime =
                    Math.Max(
                        0,
                        value);
        }

        public void InvalidateCapture()
        {
            _captureDirty =
                true;

        }

        [Browsable(false)]
        [DesignerSerializationVisibility(
            DesignerSerializationVisibility.Hidden)]
        public float Radius
        {
            get => _radius;

            set
            {
                float newValue =
                    Math.Clamp(
                        value,
                        20f,
                        1024f);

                if (Math.Abs(
                        _radius -
                        newValue) <
                    0.001f)
                {
                    return;
                }

                _radius =
                    newValue;

                RebuildLightLut();

                EnsureOutputDib();
            }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(
            DesignerSerializationVisibility.Hidden)]
        public Color LightColor
        {
            get => _lightColor;

            set =>
                _lightColor =
                    value;
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(
            DesignerSerializationVisibility.Hidden)]
        public float LightStrength
        {
            get => _lightStrength;

            set =>
                _lightStrength =
       value;
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(
            DesignerSerializationVisibility.Hidden)]
        public float EdgeStrength
        {
            get => _edgeStrength;

            set =>
                _edgeStrength =
                    value;
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(
            DesignerSerializationVisibility.Hidden)]
        public float EdgeWidth
        {
            get => _edgeWidth;

            set =>
                _edgeWidth =
                    value;
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(
            DesignerSerializationVisibility.Hidden)]
        public bool IsMouseLight
        {
            get => _isMouseLight;

            set =>
                _isMouseLight =
                    value;
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(
            DesignerSerializationVisibility.Hidden)]
        public float PresentationDelayMs
        {
            get => _presentationDelayMs;

            set =>
                _presentationDelayMs =
                    Math.Max(
                        0f,
                        value);
        }

        #endregion

        #region Window

        protected override bool ShowWithoutActivation =>
            true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp =
                    base.CreateParams;

                cp.ExStyle |=
                    WS_EX_LAYERED |
                    WS_EX_TRANSPARENT |
                    WS_EX_TOOLWINDOW |
                    WS_EX_NOACTIVATE;

                return cp;
            }
        }

        protected override void WndProc(
            ref Message m)
        {
            if (m.Msg ==
                WM_NCHITTEST)
            {
                m.Result =
                    (IntPtr)HTTRANSPARENT;

                return;
            }

            if (m.Msg ==
                WM_MOUSEACTIVATE)
            {
                m.Result =
                    (IntPtr)MA_NOACTIVATE;

                return;
            }

            base.WndProc(
                ref m);
        }

        #endregion

        #region Render Driver

        private void PostRenderTick(
            object? state)
        {
            if (!_running)
                return;

            if (_tickPosted)
                return;

            _tickPosted = true;

            try
            {
                _syncContext.Post(
                    RunTick,
                    null);
            }
            catch
            {
                _tickPosted = false;
            }
        }

        private void RunTick(
            object? state)
        {
            _tickPosted = false;

            if (IsDisposed)
                return;

            Timer_TickCore();
        }

        private void Timer_TickCore()
        {
            if (!_running)
                return;

            if (_targetForm.IsDisposed)
                return;

            if (!_targetForm.IsHandleCreated)
                return;

            if (!GetCursorPos(
                    out POINT mouseScreen))
            {
                return;
            }

            _mouseScreenPosition =
                mouseScreen;

            long stateTimestamp =
                Stopwatch.GetTimestamp();

            UpdateEffectState(
                mouseScreen,
                stateTimestamp);

            /*
             * ========================================================
             * 1. Screen -> Client
             * ========================================================
             *
             * 仍然使用 WinForms 的坐标转换。
             *
             * 关键区别：
             *
             * 我们现在不再假设转换是完美可逆的。
             */
            POINT mouseClient =
                mouseScreen;

            if (!ScreenToClient(
                    _targetForm.Handle,
                    ref mouseClient))
            {
                return;
            }

            UpdateCoordinateCorrection();

            if (!GetClientRect(
                    _targetForm.Handle,
                    out RECT clientRect))
            {
                return;
            }

            int clientWidth =
                clientRect.Right -
                clientRect.Left;

            int clientHeight =
                clientRect.Bottom -
                clientRect.Top;

            if (clientWidth <= 0 ||
                clientHeight <= 0)
            {
                return;
            }

            /*
             * 判断鼠标是否在 Client 区域。
             */
            bool inside =
                mouseClient.X >= 0 &&
                mouseClient.Y >= 0 &&
                mouseClient.X < clientWidth &&
                mouseClient.Y < clientHeight;

            if (!inside)
            {
                ResetMouseSpeed();

                if (_mouseInside)
                {
                    _mouseInside = false;

                    ClearOverlay();
                }

                return;
            }

            _mouseInside = true;

            if (_effectSuspended)
            {
                if (_hasValidFrame)
                {
                    ClearOverlay();
                }

                return;
            }

            if (_frameStopwatch.Elapsed
                    .TotalMilliseconds <
                _minFrameMilliseconds)
            {
                return;
            }

            _frameStopwatch.Restart();

            if (!Visible)
            {
                Show(_targetForm);
            }

            Render(
                mouseClient,
                clientWidth,
                clientHeight,
                _effectOpacity);
        }

        #endregion

        #region Native Resource Creation

        private void CreateNativeResources()
        {
            if (_sourceDc ==
                IntPtr.Zero)
            {
                _sourceDc =
                    CreateCompatibleDC(
                        IntPtr.Zero);
            }

            if (_outputDc ==
                IntPtr.Zero)
            {
                _outputDc =
                    CreateCompatibleDC(
                        IntPtr.Zero);
            }

            EnsureOutputDib();
        }

        private int GetOutputBufferSize()
        {
            int radius =
                (int)Math.Ceiling(
                    _radius);

            return radius * 2 + 1;
        }

        private void EnsureOutputDib()
        {
            int size =
                GetOutputBufferSize();

            if (_outputBitmap !=
                    IntPtr.Zero &&
                _outputWidth == size &&
                _outputHeight == size)
            {
                EnsureOutputBuffer(
                    size);

                return;
            }

            if (!ReplaceDib(
                    ref _outputBitmap,
                    ref _outputBits,
                    _outputDc,
                    size,
                    size))
            {
                return;
            }

            _outputWidth =
                size;

            _outputHeight =
                size;

            EnsureOutputBuffer(
                size);
        }

        private void EnsureOutputBuffer(
            int size)
        {
            int required =
                checked(
                    size *
                    size *
                    4);

            if (_outputBuffer.Length <
                required)
            {
                _outputBuffer =
                    new byte[required];
            }
        }

        private void EnsureSourceDib(
            int width,
            int height)
        {
            if (width <= 0 ||
                height <= 0)
            {
                return;
            }

            if (_sourceBitmap !=
                    IntPtr.Zero &&
                _sourceWidth == width &&
                _sourceHeight == height)
            {
                EnsureSourceBuffers(
                    width,
                    height);

                return;
            }

            if (!ReplaceDib(
                    ref _sourceBitmap,
                    ref _sourceBits,
                    _sourceDc,
                    width,
                    height))
            {
                return;
            }

            _sourceWidth =
                width;

            _sourceHeight =
                height;

            _captureDirty =
                true;


            EnsureSourceBuffers(
                width,
                height);
        }

        private void EnsureSourceBuffers(
            int width,
            int height)
        {
            int required =
                checked(
                    width *
                    height *
                    4);

            if (_sourceBuffer.Length <
                required)
            {
                _sourceBuffer =
                    new byte[required];
            }

            int lumaRequired =
                checked(
                    width *
                    height);

            if (_lumaBuffer.Length <
                lumaRequired)
            {
                _lumaBuffer =
                    new float[
                        lumaRequired];
            }
        }

        private bool ReplaceDib(
            ref IntPtr bitmap,
            ref IntPtr bits,
            IntPtr dc,
            int width,
            int height)
        {
            if (dc == IntPtr.Zero ||
                width <= 0 ||
                height <= 0)
            {
                return false;
            }

            BITMAPINFO info =
                new BITMAPINFO();

            info.bmiHeader.biSize =
                (uint)Marshal.SizeOf<
                    BITMAPINFOHEADER>();

            info.bmiHeader.biWidth =
                width;

            /*
             * Top-down DIB。
             *
             * bitmap row 0 =
             * visual top row。
             */
            info.bmiHeader.biHeight =
                -height;

            info.bmiHeader.biPlanes =
                1;

            info.bmiHeader.biBitCount =
                32;

            info.bmiHeader.biCompression =
                0;

            info.bmiHeader.biSizeImage =
                (uint)(
                    width *
                    height *
                    4);

            IntPtr newBitmap =
                CreateDIBSection(
                    IntPtr.Zero,
                    ref info,
                    DIB_RGB_COLORS,
                    out IntPtr newBits,
                    IntPtr.Zero,
                    0);

            if (newBitmap ==
                IntPtr.Zero)
            {
                return false;
            }

            IntPtr oldBitmap =
                SelectObject(
                    dc,
                    newBitmap);

            /*
             * 第一次创建时：
             *
             * bitmap == IntPtr.Zero
             *
             * oldBitmap 是 DC 的默认 bitmap，
             * 不能删除。
             *
             * 后续替换时：
             *
             * bitmap != IntPtr.Zero
             *
             * oldBitmap 才是我们之前创建的 bitmap。
             */
            if (bitmap !=
                    IntPtr.Zero &&
                oldBitmap !=
                    IntPtr.Zero &&
                oldBitmap !=
                    newBitmap)
            {
                DeleteObject(
                    oldBitmap);
            }

            bitmap =
                newBitmap;

            bits =
                newBits;

            return true;
        }

        private void RebuildLightLut()
        {
            int radius =
                (int)Math.Ceiling(
                    _radius);

            _lutRadius =
                radius;

            int radiusSquared =
                checked(
                    radius *
                    radius);

            _lightLut =
                new float[
                    radiusSquared + 1];

            for (int d2 = 0;
                 d2 <= radiusSquared;
                 d2++)
            {
                float normalized =
                    MathF.Sqrt(
                        d2) /
                    _radius;

                _lightLut[d2] =
                    SmoothStep(
                        1f -
                        normalized);
            }
        }

        private void RaiseTimerResolution()
        {
            if (_timerResolutionRaised)
                return;

            timeBeginPeriod(
                1);

            _timerResolutionRaised =
                true;
        }

        private void UpdateEffectState(
            POINT mouseScreen,
            long now)
        {
            bool moved =
                !_hasMouseSample ||
                mouseScreen.X !=
                    _lastMouseScreenPosition.X ||
                mouseScreen.Y !=
                    _lastMouseScreenPosition.Y;

            if (!_hasMouseSample)
            {
                _effectOpacity =
                    1f;
            }

            double speed =
                0d;

            if (moved &&
                _hasMouseSample)
            {
                double elapsedSeconds =
                    (now - _lastMouseTimestamp) /
                    (double)Stopwatch.Frequency;

                int dx =
                    mouseScreen.X -
                    _lastMouseScreenPosition.X;

                int dy =
                    mouseScreen.Y -
                    _lastMouseScreenPosition.Y;

                double distance =
                    Math.Sqrt(
                        (double)dx *
                        dx +
                        (double)dy *
                        dy);

                speed =
                    elapsedSeconds > 0
                        ? distance /
                          elapsedSeconds
                        : double.PositiveInfinity;
            }

            bool tooFast =
                speed >
                _disableWhenMouseSpeedGetTooFast;

            if (tooFast ||
                (_effectAnimating && moved))
            {
                _effectSuspended =
                    true;

                _effectAnimating =
                    false;

                _effectOpacity =
                    0f;

                _effectReadyTimestamp =
                    now +
                    (long)(
                        Stopwatch.Frequency *
                        _eDelay /
                        1000.0);

                CancelPendingRender();
            }
            else if (_effectSuspended &&
                     !moved &&
                     now >= _effectReadyTimestamp)
            {
                _effectSuspended =
                    false;

                _effectAnimating =
                    _eAnimationTime > 0;

                _animationStartTimestamp =
                    now;

                _effectOpacity =
                    _effectAnimating
                        ? 0f
                        : 1f;
            }

            if (_effectAnimating &&
                !moved)
            {
                double animationElapsed =
                    (now - _animationStartTimestamp) *
                    1000.0 /
                    Stopwatch.Frequency;

                _effectOpacity =
                    Math.Clamp(
                        (float)(
                            animationElapsed /
                            _eAnimationTime),
                        0f,
                        1f);

                if (_effectOpacity >= 1f)
                {
                    _effectAnimating =
                        false;
                }
            }

            _lastMouseScreenPosition =
                mouseScreen;

            _hasMouseSample =
                true;

            _lastMouseTimestamp =
                now;
        }

        private int _disableWhenMouseSpeedGetTooFast =
            800;

        private void ResetMouseSpeed()
        {
            _hasMouseSample =
                false;

            _lastMouseTimestamp =
                0;

            _effectSuspended =
                false;

            _effectAnimating =
                false;

            _effectOpacity =
                1f;
        }

        private void CancelPendingRender()
        {
            lock (_renderCancellationLock)
            {
                _renderGeneration++;

                if (_renderCancellation == null)
                {
                    return;
                }

                try
                {
                    _renderCancellation.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    _renderCancellation =
                        null;
                }
            }
        }

        private void FinishRender(
            CancellationTokenSource cancellation)
        {
            lock (_renderCancellationLock)
            {
                if (ReferenceEquals(
                        _renderCancellation,
                        cancellation))
                {
                    _renderCancellation =
                        null;
                }

                _renderBusy =
                    false;

                cancellation.Dispose();
            }
        }

        private void LowerTimerResolution()
        {
            if (!_timerResolutionRaised)
                return;

            timeEndPeriod(
                1);

            _timerResolutionRaised =
                false;
        }

        #endregion

        #region Target Form

        private void TargetForm_Move(
            object? sender,
            EventArgs e)
        {
            if (!_hasValidFrame)
                return;

            if (!IsHandleCreated)
                return;

            /*
             * Form 移动后重新测一次坐标误差。
             */
            UpdateCoordinateCorrection();

            /*
             * 这里 _lastOffsetX/Y 还是 Client 坐标。
             */
            if (!TryGetClientOriginScreen(
                    out POINT clientOrigin))
            {
                return;
            }

            UpdateLayeredWindowScreen(
                clientOrigin.X +
                _lastOffsetX,

                clientOrigin.Y +
                _lastOffsetY,

                _lastWidth,
                _lastHeight);
        }

        private void TargetForm_Resize(
            object? sender,
            EventArgs e)
        {
            _captureDirty =
                true;

            /*
             * 下一帧会自动重新获取 ClientSize。
             */
        }

        private void TargetForm_Invalidated(
            object? sender,
            InvalidateEventArgs e)
        {
            _captureDirty =
                true;
        }

        private void TargetForm_FormClosed(
            object? sender,
            FormClosedEventArgs e)
        {
            _running = false;

            if (!IsDisposed)
            {
                Close();
            }
        }

        #endregion

        #region Coordinate Correction

        private void UpdateCoordinateCorrection()
        {
            if (_targetForm.IsDisposed ||
                !_targetForm.IsHandleCreated)
            {
                return;
            }

            /*
             * 使用原生 ClientToScreen 映射来获得更稳定且精确的
             * 客户区 -> 屏幕坐标差异。以前通过鼠标点做 round-trip
             * 计算在鼠标位于窗体外部或控件绘制方式不同的情况下
             * 会产生 1px 左右的偏移。改为直接查询 0,0 的原生映射
             * 可以消除这种下/右偏移并保持鼠标光晕效果不变。
             */
            Point managedOrigin =
                _targetForm.PointToScreen(
                    Point.Empty);

            POINT nativeOrigin =
                new POINT(0, 0);

            bool nativeOk =
                ClientToScreen(
                    _targetForm.Handle,
                    ref nativeOrigin);

            if (nativeOk)
            {
                _coordinateCorrectionX =
                    nativeOrigin.X -
                    managedOrigin.X;

                _coordinateCorrectionY =
                    nativeOrigin.Y -
                    managedOrigin.Y;
            }
            else
            {
                /* 回退到以前的鼠标 round-trip 方法（极少数情况） */
                if (!GetCursorPos(
                        out POINT mouseScreen))
                {
                    return;
                }

                Point mouseClient =
                    _targetForm.PointToClient(
                        new Point(
                            mouseScreen.X,
                            mouseScreen.Y));

                Point roundTripScreen =
                    _targetForm.PointToScreen(
                        mouseClient);

                _coordinateCorrectionX =
                    mouseScreen.X -
                    roundTripScreen.X;

                _coordinateCorrectionY =
                    mouseScreen.Y -
                    roundTripScreen.Y;
            }

            if (_coordinateCorrectionX !=
                    _lastCoordinateCorrectionX ||
                _coordinateCorrectionY !=
                    _lastCoordinateCorrectionY)
            {
                _lastCoordinateCorrectionX =
                    _coordinateCorrectionX;

                _lastCoordinateCorrectionY =
                    _coordinateCorrectionY;

                Debug.WriteLine(
                    "TangerineLight coordinate " +
                    $"correction = " +
                    $"({_coordinateCorrectionX}, " +
                    $"{_coordinateCorrectionY})");
            }
        }

        private bool TryGetClientOriginScreen(
            out POINT origin)
        {
            origin = new POINT(0, 0);

            return !_targetForm.IsDisposed &&
                _targetForm.IsHandleCreated &&
                ClientToScreen(
                    _targetForm.Handle,
                    ref origin);
        }

        #endregion
        public void Reload()
        {
            if (IsDisposed ||
                _targetForm.IsDisposed ||
                !_targetForm.IsHandleCreated)
            {
                return;
            }

            // 丢弃当前捕获画面
            _captureDirty = true;

            // 强制下一次重新计算坐标
            UpdateCoordinateCorrection();

            // 如果 Overlay 当前不可见，重新显示
            if (!Visible)
            {
                Show(_targetForm);
            }

            // 让 Overlay 立即参与下一轮绘制
            Invalidate();
        }
        #region Render

        private async void Render(
            POINT mouseClient,
            int clientWidth,
            int clientHeight,
            float opacity)
        {
            if (_renderBusy)
            {
                return;
            }

            _renderBusy =
                true;

            CancellationTokenSource cancellation =
                new CancellationTokenSource();

            _renderCancellation =
                cancellation;

            int generation =
                ++_renderGeneration;

            CancellationToken token =
                cancellation.Token;

            EnsureOutputDib();

            EnsureSourceDib(
                clientWidth,
                clientHeight);

            if (_sourceBits ==
                    IntPtr.Zero ||
                _outputBits ==
                    IntPtr.Zero)
            {
                FinishRender(
                    cancellation);
                return;
            }

            int radius =
                _lutRadius;

            /*
             * 所有坐标都属于：
             *
             * TARGET CLIENT
             */
            int left =
                Math.Max(
                    0,
                    mouseClient.X -
                    radius);

            int top =
                Math.Max(
                    0,
                    mouseClient.Y -
                    radius);

            int right =
                Math.Min(
                    clientWidth - 1,
                    mouseClient.X +
                    radius);

            int bottom =
                Math.Min(
                    clientHeight - 1,
                    mouseClient.Y +
                    radius);

            int width =
                right -
                left +
                1;

            int height =
                bottom -
                top +
                1;

            if (width < 3 ||
                height < 3)
            {
                ClearOverlay();
                FinishRender(
                    cancellation);
                return;
            }

            if (width > _outputWidth ||
                height > _outputHeight)
            {
                FinishRender(
                    cancellation);
                return;
            }

            /*
             * ========================================================
             * Capture
             * ========================================================
             *
             * 完全保留原来的 WM_PRINT。
             *
             * source DIB：
             *
             *     [0,0]
             *
             * 对应：
             *
             *     target client [0,0]
             */
            CaptureTarget();

            int sourceStride =
                _sourceWidth *
                4;

            int rowBytes =
                width *
                4;

            /*
             * 只复制光效实际需要的区域。
             *
             * WM_PRINT 本身仍然绘制整个 client，
             * 但后面的 CPU 数据处理只处理半径区域。
             */
            for (int row = 0;
                 row < height;
                 row++)
            {
                IntPtr sourceRow =
                    IntPtr.Add(
                        _sourceBits,
                        (top + row) *
                        sourceStride +
                        left * 4);

                Marshal.Copy(
                    sourceRow,
                    _sourceBuffer,
                    row *
                    rowBytes,
                    rowBytes);
            }

            /*
             * ========================================================
             * Luma
             * ========================================================
             */
            bool useEdges =
                _edgeStrength >
                0.0001f;

            for (int row = 0;
                 row < height;
                 row++)
            {
                Array.Clear(
                    _outputBuffer,
                    row *
                    rowBytes,
                    rowBytes);
            }

            /*
             * mouse 在 output bitmap 中的位置。
             */
            int mouseX =
                mouseClient.X -
                left;

            int mouseY =
                mouseClient.Y -
                top;

            int radiusSquared =
                radius *
                radius;

            byte colorB =
                _lightColor.B;

            byte colorG =
                _lightColor.G;

            byte colorR =
                _lightColor.R;

            float edgeWidthFactor =
                1f;

            if (_edgeWidth > 1f)
            {
                edgeWidthFactor =
                    Math.Clamp(
                        1f /
                        _edgeWidth +
                        0.5f,
                        0.5f,
                        1f);
            }

            /*
             * ========================================================
             * 光效核心算法
             * ========================================================
             *
             * 保留原来的：
             *
             *     Distance LUT
             *     Mouse Light
             *     Sobel Edge
             *     Edge Width
             *     Premultiplied Alpha
             */
            Action<int, int> RenderRows =
                (yStart, yEndExclusive) =>
                {
                    for (int y = yStart;
                         y < yEndExclusive;
                         y++)
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        int dy =
                            y -
                            mouseY;

                        int dySquared =
                            dy *
                            dy;

                        if (dySquared >
                            radiusSquared)
                        {
                            continue;
                        }

                        int span =
                            (int)MathF.Sqrt(
                                radiusSquared -
                                dySquared);

                        int startX =
                            Math.Max(
                                1,
                                mouseX -
                                span);

                        int endX =
                            Math.Min(
                                width - 2,
                                mouseX +
                                span);

                        int rowBase =
                            y *
                            width;

                        for (int x = startX;
                             x <= endX;
                             x++)
                        {
                            int dx =
                                x -
                                mouseX;

                            int distanceSquared =
                                dx * dx +
                                dySquared;

                            if (distanceSquared >
                                radiusSquared)
                            {
                                continue;
                            }

                            float light =
                                _lightLut[
                                    distanceSquared];

                            if (light <=
                                0.0001f)
                            {
                                continue;
                            }

                            float alpha =
                                0f;

                            if (_isMouseLight)
                            {
                                alpha =
                                    light *
                                    _lightStrength;
                            }

                            if (useEdges)
                            {
                                int index =
                                    rowBase +
                                    x;

                                float edge =
                                    CalculateSobelEdge(
                                        _lumaBuffer,
                                        width,
                                        index);

                                if (edge > 0f)
                                {
                                    alpha +=
                                        edge *
                                        _edgeStrength *
                                        light *
                                        edgeWidthFactor;
                                }
                            }

                            if (alpha <=
                                0.001f)
                            {
                                continue;
                            }

                            if (alpha > 1f)
                            {
                                alpha = 1f;
                            }

                            int outputIndex =
                                (rowBase + x) *
                                4;

                            /*
                             * Premultiplied BGRA
                             */
                            _outputBuffer[
                                outputIndex] =
                                (byte)(
                                    colorB *
                                    alpha);

                            _outputBuffer[
                                outputIndex + 1] =
                                (byte)(
                                    colorG *
                                    alpha);

                            _outputBuffer[
                                outputIndex + 2] =
                                (byte)(
                                    colorR *
                                    alpha);

                            _outputBuffer[
                                outputIndex + 3] =
                                (byte)(
                                    alpha *
                                    255f);
                        }
                    }
                };

            /*
             * 小区域没必要并行。
             *
             * 大区域按块并行。
             */
            try
            {
                await Task.Run(
                    () =>
                    {
                        if (useEdges)
                        {
                            BuildLuma(
                                width,
                                height);
                        }

                        RunInStrips(
                            1,
                            height - 1,
                            RenderRows);
                    });
            }
            catch (OperationCanceledException)
            {
                FinishRender(
                    cancellation);
                return;
            }

            if (token.IsCancellationRequested ||
                generation != _renderGeneration)
            {
                FinishRender(
                    cancellation);
                return;
            }

            /*
             * ========================================================
             * 写入 output DIB
             * ========================================================
             */
            int outputStride =
                _outputWidth *
                4;

            for (int row = 0;
                 row < height;
                 row++)
            {
                IntPtr destinationRow =
                    IntPtr.Add(
                        _outputBits,
                        row *
                        outputStride);

                Marshal.Copy(
                    _outputBuffer,
                    row *
                    rowBytes,
                    destinationRow,
                    rowBytes);
            }

            /*
             * 保存 Client 坐标。
             */
            _lastOffsetX =
                left;

            _lastOffsetY =
                top;

            _lastWidth =
                width;

            _lastHeight =
                height;

            _hasValidFrame =
                true;

            /*
             * 取得当前 Target Client (0,0)
             * 的实际 Screen 坐标。
             */
            if (!TryGetClientOriginScreen(
                    out POINT origin))
            {
                FinishRender(
                    cancellation);
                return;
            }

            /*
             * 这里不要重新 PointToScreen(left, top)
             * 再做第二套数学。
             *
             * 因为 left/top 本来就是 Client 坐标。
             */
            int screenX =
                origin.X +
                left;

            int screenY =
                origin.Y +
                top;

            UpdateLayeredWindowScreen(
                screenX,
                screenY,
                width,
                height,
                (byte)Math.Clamp(
                    (int)(opacity * 255f),
                    0,
                    255));

            FinishRender(
                cancellation);
        }

        #endregion

        #region Luma

        private void BuildLuma(
            int width,
            int height)
        {
            int rowBytes =
                width *
                4;

            Action<int, int> BuildRows =
                (begin, endExclusive) =>
                {
                    for (int y = begin;
                         y < endExclusive;
                         y++)
                    {
                        int sourceRow =
                            y *
                            rowBytes;

                        int lumaRow =
                            y *
                            width;

                        for (int x = 0;
                             x < width;
                             x++)
                        {
                            int p =
                                sourceRow +
                                x * 4;

                            _lumaBuffer[
                                lumaRow + x] =

                                _sourceBuffer[
                                    p + 2] *
                                0.299f +

                                _sourceBuffer[
                                    p + 1] *
                                0.587f +

                                _sourceBuffer[
                                    p] *
                                0.114f;
                        }
                    }
                };

            RunInStrips(
                0,
                height,
                BuildRows);
        }

        #endregion

        #region Parallel

        private void RunInStrips(
            int begin,
            int endExclusive,
            Action<int, int> action)
        {
            int rowCount =
                endExclusive -
                begin;

            if (rowCount <= 0)
                return;

            /*
             * 少于 32 行：
             * 直接跑。
             */
            if (rowCount < 32 ||
                _processorCount <= 1)
            {
                action(
                    begin,
                    endExclusive);

                return;
            }

            /*
             * 每 32 行作为一个工作块。
             *
             * 不再每行创建一个 Parallel task。
             */
            int stripCount =
                Math.Min(
                    _processorCount,
                    Math.Max(
                        1,
                        (rowCount +
                         31) /
                        32));

            if (stripCount <= 1)
            {
                action(
                    begin,
                    endExclusive);

                return;
            }

            int rowsPerStrip =
                (rowCount +
                 stripCount -
                 1) /
                stripCount;

            Parallel.For(
                0,
                stripCount,
                strip =>
                {
                    int start =
                        begin +
                        strip *
                        rowsPerStrip;

                    int end =
                        Math.Min(
                            endExclusive,
                            start +
                            rowsPerStrip);

                    if (start < end)
                    {
                        action(
                            start,
                            end);
                    }
                });
        }

        #endregion

        #region Capture

        private void CaptureTarget()
        {
            if (_sourceDc == IntPtr.Zero ||
                _targetForm.IsDisposed ||
                !_targetForm.IsHandleCreated)
            {
                return;
            }

            if (!_captureDirty)
            {
                return;
            }

            /*
             * 优先让目标窗体把客户区绘制到 source DIB。
             * 这样不需要隐藏 layered window，也不会把上一帧光效
             * 再次采集回来。PW_CLIENTONLY 保证 source [0,0] 与
             * target client [0,0] 对齐。
             */
            if (PrintWindow(
                    _targetForm.Handle,
                    _sourceDc,
                    PW_CLIENTONLY))
            {
                _captureDirty =
                    false;

                return;
            }

            /*
             * Overlay 是独立的 layered window。
             * 仅在 PrintWindow 不支持目标窗体时才回退到屏幕捕获。
             */
            bool wasVisible =
                Visible;

            if (wasVisible)
            {
                Hide();
            }

            try
            {
                if (!TryGetClientOriginScreen(
                        out POINT origin))
                {
                    return;
                }

                using Graphics graphics =
                    Graphics.FromHdc(
                        _sourceDc);

                graphics.CopyFromScreen(
                    origin.X,
                    origin.Y,
                    0,
                    0,
                    new Size(
                        _sourceWidth,
                        _sourceHeight),
                    CopyPixelOperation.SourceCopy);

                _captureDirty =
                    false;

            }
            finally
            {
                if (wasVisible &&
                    !IsDisposed)
                {
                    Show(_targetForm);
                }
            }
        }

        #endregion

        #region Sobel

        private static float CalculateSobelEdge(
            float[] luma,
            int width,
            int index)
        {
            float gx =
                luma[
                    index -
                    width +
                    1] -

                luma[
                    index -
                    width -
                    1] +

                2f *
                (
                    luma[
                        index + 1] -
                    luma[
                        index - 1]
                ) +

                luma[
                    index +
                    width +
                    1] -

                luma[
                    index +
                    width -
                    1];

            float gy =
                luma[
                    index +
                    width -
                    1] -

                luma[
                    index -
                    width -
                    1] +

                2f *
                (
                    luma[
                        index +
                        width] -

                    luma[
                        index -
                        width]
                ) +

                luma[
                    index +
                    width +
                    1] -

                luma[
                    index -
                    width +
                    1];

            float magnitudeSquared =
                gx * gx +
                gy * gy;

            const float threshold =
                0.10f;

            /*
             * 102^2
             *
             * 与 normalized threshold = 0.10
             * 保持一致。
             */
            const float thresholdSquared =
                10404f;

            if (magnitudeSquared <=
                thresholdSquared)
            {
                return 0f;
            }

            float magnitude =
                MathF.Sqrt(
                    magnitudeSquared);

            float edge =
                magnitude /
                1020f;

            edge =
                (edge -
                 threshold) /
                (1f -
                 threshold);

            /*
             * 平方：
             *
             * 弱边缘压低。
             * 强边缘保留。
             */
            edge *= edge;

            return Math.Clamp(
                edge,
                0f,
                1f);
        }

        #endregion

        #region Math

        private static float SmoothStep(
            float value)
        {
            value =
                Math.Clamp(
                    value,
                    0f,
                    1f);

            return
                value *
                value *
                (3f -
                 2f *
                 value);
        }

        #endregion

        #region Layered Window

        private void ClearOverlay()
        {
            if (!IsHandleCreated)
                return;

            if (_outputBits ==
                IntPtr.Zero)
            {
                return;
            }

            /*
             * 真正清空 Output DIB。
             *
             * 原来只写一个像素，
             * 如果下一次更新区域扩大，
             * 旧数据可能残留。
             */
            int totalBytes =
                checked(
                    _outputWidth *
                    _outputHeight *
                    4);

            int bytesToClear =
                Math.Min(
                    totalBytes,
                    _outputBuffer.Length);

            Array.Clear(
                _outputBuffer,
                0,
                bytesToClear);

            if (bytesToClear > 0)
            {
                Marshal.Copy(
                    _outputBuffer,
                    0,
                    _outputBits,
                    bytesToClear);
            }

            _hasValidFrame =
                false;

            /*
             * 清除时使用当前 target client 原点。
             */
            if (!TryGetClientOriginScreen(
                    out POINT origin))
            {
                return;
            }

            UpdateLayeredWindowScreen(
                origin.X +
                0,

                origin.Y +
                0,

                1,
                1);
        }

        private void UpdateLayeredWindowScreen(
            int screenX,
            int screenY,
            int width,
            int height,
            byte sourceAlpha = 255)
        {
            if (!IsHandleCreated)
                return;

            if (_targetForm.IsDisposed)
                return;

            if (_outputDc ==
                    IntPtr.Zero ||
                _outputBitmap ==
                    IntPtr.Zero)
            {
                return;
            }

            if (width <= 0 ||
                height <= 0)
            {
                return;
            }

            POINT destination =
                new POINT(
                    screenX,
                    screenY);

            POINT source =
                new POINT(
                    0,
                    0);

            SIZE size =
                new SIZE(
                    width,
                    height);

            BLENDFUNCTION blend =
                new BLENDFUNCTION
                {
                    BlendOp =
                        AC_SRC_OVER,

                    BlendFlags =
                        0,

                    SourceConstantAlpha =
                        sourceAlpha,

                    AlphaFormat =
                        AC_SRC_ALPHA
                };

            IntPtr screenDc =
                GetDC(
                    IntPtr.Zero);

            if (screenDc ==
                IntPtr.Zero)
            {
                return;
            }

            try
            {
                bool result =
                    UpdateLayeredWindow(
                        Handle,
                        screenDc,
                        ref destination,
                        ref size,
                        _outputDc,
                        ref source,
                        0,
                        ref blend,
                        ULW_ALPHA);

                if (!result)
                {
                    Debug.WriteLine(
                        "TangerineLight: " +
                        "UpdateLayeredWindow failed. " +
                        $"Error={Marshal.GetLastWin32Error()}");
                }
            }
            finally
            {
                ReleaseDC(
                    IntPtr.Zero,
                    screenDc);
            }
        }

        #endregion

        #region Start / Stop

        public void Start()
        {
            if (IsDisposed)
                return;

            _running = true;

            RaiseTimerResolution();

            _frameStopwatch.Restart();

            UpdateCoordinateCorrection();

            if (!Visible)
            {
                Show(_targetForm);
            }
        }

        public void Stop()
        {
            _running = false;

            LowerTimerResolution();

            ClearOverlay();
        }

        #endregion

        #region Dispose

        protected override void Dispose(
            bool disposing)
        {
            if (disposing)
            {
                _running = false;

                _renderDriver.Dispose();

                _targetForm.Move -=
                    TargetForm_Move;

                _targetForm.Resize -=
                    TargetForm_Resize;

                _targetForm.FormClosed -=
                    TargetForm_FormClosed;

                LowerTimerResolution();

                /*
                 * ====================================================
                 * 释放 GDI bitmap
                 * ====================================================
                 *
                 * 必须先把 bitmap 从 DC 中解除选择。
                 */
                if (_sourceDc !=
                        IntPtr.Zero &&
                    _sourceBitmap !=
                        IntPtr.Zero)
                {
                    SelectObject(
                        _sourceDc,
                        IntPtr.Zero);
                }

                if (_outputDc !=
                        IntPtr.Zero &&
                    _outputBitmap !=
                        IntPtr.Zero)
                {
                    SelectObject(
                        _outputDc,
                        IntPtr.Zero);
                }

                if (_sourceBitmap !=
                    IntPtr.Zero)
                {
                    DeleteObject(
                        _sourceBitmap);

                    _sourceBitmap =
                        IntPtr.Zero;

                    _sourceBits =
                        IntPtr.Zero;
                }

                if (_outputBitmap !=
                    IntPtr.Zero)
                {
                    DeleteObject(
                        _outputBitmap);

                    _outputBitmap =
                        IntPtr.Zero;

                    _outputBits =
                        IntPtr.Zero;
                }

                if (_sourceDc !=
                    IntPtr.Zero)
                {
                    DeleteDC(
                        _sourceDc);

                    _sourceDc =
                        IntPtr.Zero;
                }

                if (_outputDc !=
                    IntPtr.Zero)
                {
                    DeleteDC(
                        _outputDc);

                    _outputDc =
                        IntPtr.Zero;
                }

                _sourceBuffer =
                    Array.Empty<byte>();

                _outputBuffer =
                    Array.Empty<byte>();

                _lumaBuffer =
                    Array.Empty<float>();

                _lightLut =
                    Array.Empty<float>();
            }

            base.Dispose(
                disposing);
        }

        #endregion
    }
}
