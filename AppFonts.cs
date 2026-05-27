using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;

namespace IUnlocker;

internal static class AppFonts
{
    private static readonly PrivateFontCollection FontCollection = new();
    private static readonly List<IntPtr> FontMemory = [];
    private static readonly string FallbackFamily = "Microsoft Sans Serif";
    private static bool _loaded;

    public static string FamilyName
    {
        get
        {
            EnsureLoaded();
            return FontCollection.Families.FirstOrDefault()?.Name ?? FallbackFamily;
        }
    }

    public static Font Create(float size, FontStyle style = FontStyle.Regular)
    {
        EnsureLoaded();
        try
        {
            var family = FontCollection.Families.FirstOrDefault();
            if (family is not null && family.IsStyleAvailable(style))
            {
                return new Font(family, size, style, GraphicsUnit.Point);
            }

            if (family is not null)
            {
                return new Font(family, size, FontStyle.Regular, GraphicsUnit.Point);
            }
        }
        catch
        {
            // Fall back to a Windows stock font if bundled fonts cannot be loaded.
        }

        return new Font(FallbackFamily, size, style, GraphicsUnit.Point);
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        TryAddEmbeddedFont("NotoSans-Regular.ttf");
        TryAddEmbeddedFont("NotoSans-Bold.ttf");
    }

    private static void TryAddEmbeddedFont(string fileName)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
            if (resourceName is null)
            {
                return;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                return;
            }

            var bytes = new byte[stream.Length];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read == 0)
                {
                    break;
                }

                offset += read;
            }

            var memory = Marshal.AllocCoTaskMem(bytes.Length);
            Marshal.Copy(bytes, 0, memory, bytes.Length);
            FontMemory.Add(memory);
            FontCollection.AddMemoryFont(memory, bytes.Length);
        }
        catch
        {
            // Keep the app usable even if a font file is missing or damaged.
        }
    }
}
