using System.Runtime.InteropServices;

namespace ConfGTS.Client.Services;

internal static class RememberedCredentials
{
    private const string TargetName = "ConfGTS.Client.Login";
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;

    public static bool TryRead(out string username, out string password)
    {
        username = "";
        password = "";

        if (!CredRead(TargetName, CredTypeGeneric, 0, out var credentialPtr) || credentialPtr == IntPtr.Zero)
            return false;

        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);
            username = credential.UserName ?? "";

            if (credential.CredentialBlob != IntPtr.Zero && credential.CredentialBlobSize > 0)
            {
                password = Marshal.PtrToStringUni(
                    credential.CredentialBlob,
                    (int)credential.CredentialBlobSize / 2) ?? "";
            }

            return !string.IsNullOrWhiteSpace(username);
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    public static void Save(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
            return;

        var blob = Marshal.StringToCoTaskMemUni(password ?? "");
        try
        {
            var credential = new CREDENTIAL
            {
                Type = CredTypeGeneric,
                TargetName = TargetName,
                CredentialBlobSize = (uint)((password ?? "").Length * 2),
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = username.Trim()
            };

            if (!CredWrite(ref credential, 0))
                throw new InvalidOperationException("Не удалось сохранить учетные данные в диспетчере учетных данных Windows.");
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(blob);
        }
    }

    public static void Clear()
    {
        CredDelete(TargetName, CredTypeGeneric, 0);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite([In] ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);
}
