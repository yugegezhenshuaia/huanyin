using System;
using System.Runtime.InteropServices;

namespace HuanYin
{
    internal static class Native
    {
        [DllImport("user32.dll")]
        internal static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        /// <summary>Turns the native scrollbars of a control dark (Windows 10 1809+).</summary>
        internal static void UseDarkScrollbars(IntPtr hwnd)
        {
            try { SetWindowTheme(hwnd, "DarkMode_Explorer", null); }
            catch { }
        }

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

        internal static void UseDarkTitleBar(IntPtr hwnd)
        {
            int on = 1;
            try
            {
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));
            }
            catch { }
        }
    }

    internal enum TaskbarProgressState
    {
        NoProgress = 0,
        Indeterminate = 1,
        Normal = 2,
        Error = 4,
        Paused = 8
    }

    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);
        void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        void SetProgressState(IntPtr hwnd, TaskbarProgressState tbpFlags);
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    internal class TaskbarListClass
    {
    }

    internal sealed class TaskbarProgress : IDisposable
    {
        private ITaskbarList3 api;
        private readonly IntPtr hwnd;
        private bool disposed;

        public TaskbarProgress(IntPtr hwnd)
        {
            this.hwnd = hwnd;
            try
            {
                api = (ITaskbarList3)new TaskbarListClass();
                api.HrInit();
            }
            catch
            {
                api = null;
            }
        }

        public bool Available { get { return api != null; } }

        public void SetState(TaskbarProgressState state)
        {
            if (api == null || disposed) return;
            try { api.SetProgressState(hwnd, state); } catch { }
        }

        public void SetValue(double fraction)
        {
            if (api == null || disposed) return;
            try
            {
                if (fraction < 0) fraction = 0;
                if (fraction > 1) fraction = 1;
                api.SetProgressValue(hwnd, (ulong)(fraction * 1000.0), 1000);
            }
            catch { }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            SetState(TaskbarProgressState.NoProgress);
            try
            {
                if (api != null && Marshal.IsComObject(api)) Marshal.ReleaseComObject(api);
            }
            catch { }
            api = null;
        }
    }
}
