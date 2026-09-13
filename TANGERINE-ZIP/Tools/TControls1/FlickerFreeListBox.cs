using System;
using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TANGERINE_ZIP.Tools.TControls1
{
    internal sealed class FlickerFreeListBox : ListBox
    {
        private const int WM_VSCROLL = 0x0115;
        private const int WM_HSCROLL = 0x0114;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_KEYDOWN = 0x0100;
            
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private Func<int, Color>? _itemColorProvider;

        private bool _updatingItems;

        [DllImport(
            "uxtheme.dll",
            CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(
            IntPtr hWnd,
            string pszSubAppName,
            string? pszSubIdList);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int dwAttribute,
            ref int pvAttribute,
            int cbAttribute);

        public event EventHandler? ViewChanged;

        public FlickerFreeListBox()
        {
            /*
             * 减少重绘闪烁。
             */
            DoubleBuffered = true;
            ResizeRedraw = true;

            /*
             * 开启自绘。
             *
             * 这样每一个 Item 都可以单独设置颜色。
             */
            DrawMode =
                DrawMode.OwnerDrawFixed;
        }

        protected override void OnHandleCreated(
            EventArgs e)
        {
            base.OnHandleCreated(e);

            /*
             * Windows 深色模式。
             */
            int darkMode = 1;

            DwmSetWindowAttribute(
                Handle,
                DWMWA_USE_IMMERSIVE_DARK_MODE,
                ref darkMode,
                sizeof(int));

            /*
             * 使用 Explorer DarkMode ListBox
             * 的系统绘制风格。
             */
            SetWindowTheme(
                Handle,
                "DarkMode_Explorer",
                null);
        }

        protected override void WndProc(
            ref Message m)
        {
            /*
             * 不再拦截 WM_ERASEBKGND。
             *
             * 之前直接：
             *
             *     m.Result = 1;
             *     return;
             *
             * 会导致 Items.Clear() 后旧像素残留。
             *
             * DoubleBuffered 已经负责减少闪烁，
             * 这里让系统正常处理背景擦除。
             */
            base.WndProc(ref m);

            /*
             * 通知外部：
             *
             *     滚动
             *     鼠标滚轮
             *     键盘移动
             *
             * 视图发生变化。
             */
            if (!_updatingItems &&
                (m.Msg == WM_VSCROLL ||
                 m.Msg == WM_HSCROLL ||
                 m.Msg == WM_MOUSEWHEEL ||
                 m.Msg == WM_KEYDOWN))
            {
                ViewChanged?.Invoke(
                    this,
                    EventArgs.Empty);
            }
        }

        #region Owner Draw

        protected override void OnDrawItem(
            DrawItemEventArgs e)
        {
            if (e.Index < 0 ||
                e.Index >= Items.Count)
            {
                return;
            }

            /*
             * 先画系统选中/非选中背景。
             */
            e.DrawBackground();

            string text =
                Items[e.Index]?.ToString() ?? "";

            /*
             * 默认使用 ListBox.ForeColor。
             */
            Color textColor =
                ForeColor;

            /*
             * 如果外部提供颜色判断函数，
             * 使用外部指定的颜色。
             */
            if (_itemColorProvider != null)
            {
                textColor =
                    _itemColorProvider(e.Index);
            }

            using Brush brush =
                new SolidBrush(textColor);

            /*
             * 给文字留出少量左侧间距。
             */
            Rectangle textBounds =
                new Rectangle(
                    e.Bounds.Left + 2,
                    e.Bounds.Top,
                    Math.Max(
                        0,
                        e.Bounds.Width - 2),
                    e.Bounds.Height);

            /*
             * 垂直居中。
             */
            StringFormat stringFormat =
                new StringFormat
                {
                    LineAlignment =
                        StringAlignment.Center,

                    Alignment =
                        StringAlignment.Near,

                    Trimming =
                        StringTrimming.EllipsisCharacter,

                    FormatFlags =
                        StringFormatFlags.NoWrap
                };

            e.Graphics.DrawString(
                text,
                e.Font,
                brush,
                textBounds,
                stringFormat);

            stringFormat.Dispose();

            /*
             * 焦点框。
             */
            e.DrawFocusRectangle();
        }

        #endregion

        #region Item Color

        /*
         * 设置每一个 Item 的颜色。
         *
         * 不使用 public Func 属性，
         * 因此 WinForms Designer 不会尝试序列化它。
         */
        public void SetItemColorProvider(
            Func<int, Color>? provider)
        {
            _itemColorProvider =
                provider;

            Invalidate();
        }

        /*
         * 清除自定义颜色。
         */
        public void ClearItemColorProvider()
        {
            _itemColorProvider = null;

            Invalidate();
        }

        #endregion

        #region Item Update

        /*
         * 开始批量更新 Item。
         *
         * 相当于：
         *
         *     BeginUpdate()
         *
         * 但额外记录自己的更新状态。
         */
        public void BeginUpdateItems()
        {
            _updatingItems = true;

            BeginUpdate();
        }

        /*
         * 结束批量更新。
         */
        public void EndUpdateItems()
        {
            EndUpdate();

            _updatingItems = false;

            /*
             * 强制整个 ListBox 重绘。
             */
            Invalidate(true);
            Update();
        }

        /*
         * 一次性替换整个 Items 集合。
         *
         * 例如：
         *
         *     fileBox.ReplaceItems(items);
         */
        public void ReplaceItems(
            ICollection items)
        {
            BeginUpdateItems();

            try
            {
                Items.Clear();

                Items.AddRange(items);
            }
            finally
            {
                EndUpdateItems();
            }
        }

        /*
         * 强制刷新。
         */
        public void RefreshItems()
        {
            Invalidate(true);
            Update();
        }

        #endregion

        #region Selection

        /*
         * 清除当前选择，并立即重绘。
         */
        public void ClearSelectedAndRefresh()
        {
            ClearSelected();

            Invalidate();
            Update();
        }

        #endregion

        #region Resize

        protected override void OnResize(
            EventArgs e)
        {
            base.OnResize(e);

            Invalidate(true);
        }

        #endregion
    }
}