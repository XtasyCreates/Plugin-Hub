using System.Runtime.InteropServices;

namespace PluginHub.Services;

/// <summary>
/// Windows DPAPI for the current user: what it protects can be read back only under this
/// Windows account on this PC. That makes a copied key file useless to anyone else - and
/// is exactly why the password-protected backup exists.
/// </summary>
internal static class Dpapi
{
    private const int UiForbidden = 0x1;

    public static byte[] Protect(byte[] data, byte[] entropy) => Run(data, entropy, protect: true);

    public static byte[] Unprotect(byte[] data, byte[] entropy) => Run(data, entropy, protect: false);

    private static byte[] Run(byte[] data, byte[] entropy, bool protect)
    {
        var input  = Blob.Of(data);
        var extra  = Blob.Of(entropy);
        var output = new Blob();
        try
        {
            bool ok = protect
                ? CryptProtectData(ref input, null, ref extra, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output)
                : CryptUnprotectData(ref input, IntPtr.Zero, ref extra, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output);
            if (!ok)
                throw new HubError(protect
                    ? "Windows konnte den Schlüssel nicht verschlüsseln."
                    : "Windows konnte den Schlüssel nicht entschlüsseln – er stammt von einem anderen " +
                      "Windows-Konto oder PC. Stell ihn aus deinem Backup wieder her.");

            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            return result;
        }
        finally
        {
            input.Free();
            extra.Free();
            if (output.Data != IntPtr.Zero)
            {
                // An unprotected key passed through here - wipe it before handing the memory back
                for (int i = 0; i < output.Size; i++) Marshal.WriteByte(output.Data, i, 0);
                LocalFree(output.Data);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public int    Size;
        public IntPtr Data;

        public static Blob Of(byte[] bytes)
        {
            var blob = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(Math.Max(1, bytes.Length)) };
            Marshal.Copy(bytes, 0, blob.Data, bytes.Length);
            return blob;
        }

        public void Free()
        {
            if (Data == IntPtr.Zero) return;
            for (int i = 0; i < Size; i++) Marshal.WriteByte(Data, i, 0);
            Marshal.FreeHGlobal(Data);
            Data = IntPtr.Zero;
        }
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob dataIn, string? description, ref Blob entropy,
        IntPtr reserved, IntPtr prompt, int flags, ref Blob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref Blob dataIn, IntPtr description, ref Blob entropy,
        IntPtr reserved, IntPtr prompt, int flags, ref Blob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
