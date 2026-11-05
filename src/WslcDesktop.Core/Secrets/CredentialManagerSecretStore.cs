using System.Runtime.InteropServices;
using System.Text;

namespace WslcDesktop.Secrets;

/// <summary>Stores secrets as generic credentials named <c>WslcDesktop/secret/&lt;name&gt;</c> in the current user's Credential Manager vault.</summary>
public sealed class CredentialManagerSecretStore : ISecretStore
{
    public const string TargetPrefix = "WslcDesktop/secret/";

    // CRED_MAX_CREDENTIAL_BLOB_SIZE
    public const int MaxValueBytes = 2560;

    private const uint TypeGeneric = 1;
    private const uint PersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public IReadOnlyList<SecretInfo> List()
    {
        if (!CredEnumerate(TargetPrefix + "*", 0, out var count, out var array))
        {
            var error = Marshal.GetLastWin32Error();
            return error == ErrorNotFound ? [] : throw new InvalidOperationException($"Couldn't list secrets (Windows error {error}).");
        }

        try
        {
            var result = new List<SecretInfo>();
            for (var i = 0; i < count; i++)
            {
                var cred = Marshal.PtrToStructure<Credential>(Marshal.ReadIntPtr(array, i * IntPtr.Size));
                var target = Marshal.PtrToStringUni(cred.TargetName) ?? string.Empty;
                if (target.StartsWith(TargetPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var modified = DateTimeOffset.FromFileTime(((long)cred.LastWritten.dwHighDateTime << 32) | (uint)cred.LastWritten.dwLowDateTime);
                    result.Add(new SecretInfo(target[TargetPrefix.Length..], Marshal.PtrToStringUni(cred.Comment) ?? string.Empty, modified));
                }
            }

            return result.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        finally
        {
            CredFree(array);
        }
    }

    public string? Get(string name)
    {
        if (!CredRead(TargetPrefix + name, TypeGeneric, 0, out var ptr))
        {
            var error = Marshal.GetLastWin32Error();
            return error == ErrorNotFound ? null : throw new InvalidOperationException($"Couldn't read the secret '{name}' (Windows error {error}).");
        }

        try
        {
            var cred = Marshal.PtrToStructure<Credential>(ptr);
            var bytes = new byte[cred.CredentialBlobSize];
            if (bytes.Length > 0)
            {
                Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
            }

            try
            {
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                Array.Clear(bytes);
            }
        }
        finally
        {
            CredFree(ptr);
        }
    }

    public void Set(string name, string value, string label)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > MaxValueBytes)
        {
            Array.Clear(bytes);
            throw new InvalidOperationException($"Secret values can be at most {MaxValueBytes:N0} bytes (UTF-8).");
        }

        var blob = Marshal.AllocHGlobal(Math.Max(bytes.Length, 1));
        var target = Marshal.StringToCoTaskMemUni(TargetPrefix + name);
        var comment = Marshal.StringToCoTaskMemUni(label.Length > 250 ? label[..250] : label);
        var user = Marshal.StringToCoTaskMemUni(name);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var cred = new Credential
            {
                Type = TypeGeneric,
                TargetName = target,
                Comment = comment,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = user,
            };
            if (!CredWrite(ref cred, 0))
            {
                throw new InvalidOperationException($"Couldn't save the secret '{name}' (Windows error {Marshal.GetLastWin32Error()}).");
            }
        }
        finally
        {
            Array.Clear(bytes);
            for (var i = 0; i < bytes.Length; i++)
            {
                Marshal.WriteByte(blob, i, 0);
            }

            Marshal.FreeHGlobal(blob);
            Marshal.FreeCoTaskMem(target);
            Marshal.FreeCoTaskMem(comment);
            Marshal.FreeCoTaskMem(user);
        }
    }

    public bool Delete(string name)
    {
        if (CredDelete(TargetPrefix + name, TypeGeneric, 0))
        {
            return true;
        }

        var error = Marshal.GetLastWin32Error();
        return error == ErrorNotFound ? false : throw new InvalidOperationException($"Couldn't delete the secret '{name}' (Windows error {error}).");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredReadW")]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredWriteW")]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredDeleteW")]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredEnumerateW")]
    private static extern bool CredEnumerate(string filter, uint flags, out int count, out IntPtr credentials);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
