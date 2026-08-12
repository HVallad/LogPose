using System;
using System.Runtime.InteropServices;

namespace LogPose
{
    // Native Windows open-file dialog via comdlg32 — Unity/Mono ships no picker.
    internal static class FilePicker
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public IntPtr lpstrFile;
            public int nMaxFile;
            public string lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int flagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetOpenFileNameW(ref OpenFileName ofn);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        internal static string PickImage(string title)
        {
            const int MaxPath = 4096;
            IntPtr buffer = Marshal.AllocHGlobal(2 * MaxPath);
            try
            {
                for (int i = 0; i < MaxPath; i++)
                    Marshal.WriteInt16(buffer, i * 2, 0);
                OpenFileName ofn = new OpenFileName();
                ofn.lStructSize = Marshal.SizeOf(typeof(OpenFileName));
                ofn.hwndOwner = GetActiveWindow();
                ofn.lpstrFilter = "Images (*.png, *.jpg)\0*.png;*.jpg;*.jpeg\0All files\0*.*\0\0";
                ofn.nFilterIndex = 1;
                ofn.lpstrFile = buffer;
                ofn.nMaxFile = MaxPath;
                ofn.lpstrTitle = title;
                // OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR — NOCHANGEDIR
                // is load-bearing: the game reads Decks/ etc. via relative paths and the
                // dialog changes the process CWD by default.
                ofn.Flags = 0x00001000 | 0x00000800 | 0x00000008;
                if (!GetOpenFileNameW(ref ofn))
                    return null;
                string picked = Marshal.PtrToStringUni(buffer);
                return string.IsNullOrEmpty(picked) ? null : picked;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("File picker failed: " + e.Message);
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
