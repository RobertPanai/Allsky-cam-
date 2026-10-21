using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AllSky.Publish;

/// <summary>
/// Protezione dei segreti con DPAPI, ambito utente corrente (crypt32.dll: CryptProtectData / CryptUnprotectData).
/// Solo Windows: su altri sistemi ogni operazione lancia PlatformNotSupportedException.
/// I buffer in chiaro temporanei (memoria non gestita) vengono azzerati prima di essere liberati.
/// Firme verificate sulla documentazione Microsoft (CryptProtectData, CryptUnprotectData, CRYPTOAPI_BLOB/DATA_BLOB):
/// dwFlags e' un DWORD (uint); pDataOut va liberato con LocalFree sul campo pbData.
/// Il valore 0x1 di CRYPTPROTECT_UI_FORBIDDEN non e' scritto nella pagina consultata: e' quello di dpapi.h, da confermare su Windows.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretProtector : ISecretProtector
{
    /// <summary>CRYPTPROTECT_UI_FORBIDDEN: nessuna finestra di dialogo (il processo e' in background).</summary>
    private const uint CryptprotectUiForbidden = 0x1;

    /// <summary>DATA_BLOB (CRYPTOAPI_BLOB): cbData (DWORD) e pbData (BYTE*).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public uint cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", EntryPoint = "CryptProtectData", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn,
        string? szDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        out DataBlob pDataOut);

    [DllImport("crypt32.dll", EntryPoint = "CryptUnprotectData", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn,
        IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        out DataBlob pDataOut);

    [DllImport("kernel32.dll", EntryPoint = "LocalFree", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    /// <summary>Cifra i byte con DPAPI per l'utente corrente.</summary>
    /// <param name="plain">Dati in chiaro (non vengono modificati).</param>
    /// <exception cref="PlatformNotSupportedException">Sistema diverso da Windows.</exception>
    /// <exception cref="Win32Exception">DPAPI ha rifiutato l'operazione.</exception>
    public byte[] Protect(byte[] plain)
    {
        EnsureWindows();
        ArgumentNullException.ThrowIfNull(plain);

        IntPtr inPtr = IntPtr.Zero;
        try
        {
            inPtr = CopyToUnmanaged(plain);
            var input = new DataBlob { cbData = (uint)plain.Length, pbData = inPtr };

            if (!CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, out DataBlob output))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Protezione dei segreti (DPAPI) non riuscita.");

            try
            {
                return CopyFromBlob(output);
            }
            finally
            {
                FreeBlob(output);
            }
        }
        finally
        {
            ZeroAndFree(inPtr, plain.Length);
        }
    }

    /// <summary>Decifra i byte prodotti da Protect per l'utente corrente.</summary>
    /// <param name="protectedData">Dati cifrati (non vengono modificati).</param>
    /// <exception cref="PlatformNotSupportedException">Sistema diverso da Windows.</exception>
    /// <exception cref="Win32Exception">DPAPI non riesce a decifrare (altro utente, dati corrotti).</exception>
    public byte[] Unprotect(byte[] protectedData)
    {
        EnsureWindows();
        ArgumentNullException.ThrowIfNull(protectedData);

        IntPtr inPtr = IntPtr.Zero;
        try
        {
            inPtr = CopyToUnmanaged(protectedData);
            var input = new DataBlob { cbData = (uint)protectedData.Length, pbData = inPtr };

            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, out DataBlob output))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Decifratura dei segreti (DPAPI) non riuscita.");

            try
            {
                // Il chiamante riceve una copia gestita: deve azzerarla dopo l'uso.
                return CopyFromBlob(output);
            }
            finally
            {
                FreeBlob(output);
            }
        }
        finally
        {
            ZeroAndFree(inPtr, protectedData.Length);
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("La protezione dei segreti (DPAPI) e' disponibile solo su Windows.");
    }

    private static IntPtr CopyToUnmanaged(byte[] data)
    {
        IntPtr ptr = Marshal.AllocHGlobal(Math.Max(1, data.Length));
        Marshal.Copy(data, 0, ptr, data.Length);
        return ptr;
    }

    private static byte[] CopyFromBlob(DataBlob blob)
    {
        var result = new byte[blob.cbData];
        if (blob.cbData > 0 && blob.pbData != IntPtr.Zero)
            Marshal.Copy(blob.pbData, result, 0, (int)blob.cbData);
        return result;
    }

    /// <summary>Azzera il buffer non gestito (che puo' contenere testo in chiaro) e lo libera.</summary>
    private static void ZeroAndFree(IntPtr ptr, int length)
    {
        if (ptr == IntPtr.Zero)
            return;
        var zeros = new byte[length];
        Marshal.Copy(zeros, 0, ptr, length);
        Array.Clear(zeros);
        Marshal.FreeHGlobal(ptr);
    }

    /// <summary>Azzera il blocco restituito da DPAPI e lo libera con LocalFree.</summary>
    private static void FreeBlob(DataBlob blob)
    {
        if (blob.pbData == IntPtr.Zero)
            return;
        var zeros = new byte[blob.cbData];
        Marshal.Copy(zeros, 0, blob.pbData, (int)blob.cbData);
        Array.Clear(zeros);
        LocalFree(blob.pbData);
    }
}
