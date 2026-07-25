using System.Runtime.InteropServices;
using System.Text;

namespace IUnlocker;

internal static class VolumeUtility
{
    private const int ErrorNoMoreFiles = 18;

    public static IEnumerable<string> EnumerateVolumeRoots()
    {
        var buffer = new StringBuilder(1024);
        var handle = FindFirstVolume(buffer, buffer.Capacity);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            yield break;
        }

        try
        {
            while (true)
            {
                yield return buffer.ToString();
                buffer.Clear();
                if (FindNextVolume(handle, buffer, buffer.Capacity))
                {
                    continue;
                }

                if (Marshal.GetLastWin32Error() == ErrorNoMoreFiles)
                {
                    yield break;
                }

                yield break;
            }
        }
        finally
        {
            FindVolumeClose(handle);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstVolume(StringBuilder volumeName, int bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextVolume(IntPtr findVolumeHandle, StringBuilder volumeName, int bufferLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindVolumeClose(IntPtr findVolumeHandle);
}
