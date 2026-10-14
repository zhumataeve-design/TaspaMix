using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TaspaMix;

internal static class VideoThumbnailLoader
{
    private static readonly Guid ImageFactoryId = new("BCC18B79-BA16-442F-80C4-8A59C30C463B");

    public static ImageSource? TryGetThumbnail(string path)
    {
        IntPtr bitmap = IntPtr.Zero;
        try
        {
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref ImageFactoryId, out var factory);
            factory.GetImage(new NativeSize(240, 135), 0x8 | 0x20, out bitmap);
            if (bitmap == IntPtr.Zero) return null;
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(240, 135));
            source.Freeze();
            return source;
        }
        catch { return null; }
        finally { if (bitmap != IntPtr.Zero) DeleteObject(bitmap); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int Width; public int Height; public NativeSize(int width, int height) { Width = width; Height = height; } }

    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(NativeSize size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);
}