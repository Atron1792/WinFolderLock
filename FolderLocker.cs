using System.IO;
using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace WinFolderLock
{
    internal static class FolderLocker
    {
        // File format:
        // [8 bytes] magic = "WFLCKV1\0"
        // [4 bytes] int protectedKeyLength (little-endian)
        // [protectedKeyLength bytes] protected AES key (DPAPI LocalMachine)
        // [12 bytes] nonce
        // [16 bytes] tag
        // [remaining bytes] ciphertext

        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("WFLCKV1\0");

        public static void LockFolder(string folderPath, string lockedFilePath, bool overwriteExisting = false)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                throw new ArgumentNullException(nameof(folderPath));
            }

            if (string.IsNullOrWhiteSpace(lockedFilePath))
            {
                throw new ArgumentNullException(nameof(lockedFilePath));
            }

            if (!Directory.Exists(folderPath))
            {
                throw new DirectoryNotFoundException(folderPath);
            }

            string sessionDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinFolderLock", "Sessions", Path.GetRandomFileName());
            _ = Directory.CreateDirectory(sessionDir);
            string tempZip = Path.Combine(sessionDir, Path.GetRandomFileName() + ".zip");
            try
            {
                ZipFile.CreateFromDirectory(folderPath, tempZip, CompressionLevel.Optimal, includeBaseDirectory: false);
                byte[] plaintext = File.ReadAllBytes(tempZip);

                byte[] key = new byte[32];
                RandomNumberGenerator.Fill(key);
                byte[] protectedKey = ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser);

                byte[] nonce = new byte[12];
                RandomNumberGenerator.Fill(nonce);

                byte[] tag = new byte[16];
                byte[] ciphertext = new byte[plaintext.Length];

                using (AesGcm aesgcm = new(key, 16))
                {
                    aesgcm.Encrypt(nonce, plaintext, ciphertext, tag, null);
                }

                FileMode fileMode = overwriteExisting ? FileMode.Create : FileMode.CreateNew;
                using (FileStream fs = new(lockedFilePath, fileMode, FileAccess.Write, FileShare.None))
                using (BinaryWriter bw = new(fs))
                {
                    bw.Write(Magic);
                    bw.Write(protectedKey.Length);
                    bw.Write(protectedKey);
                    bw.Write(nonce);
                    bw.Write(tag);
                    bw.Write(ciphertext);
                }

                FileAttributes attributes = File.GetAttributes(lockedFilePath) & ~FileAttributes.Hidden & ~FileAttributes.System;
                File.SetAttributes(lockedFilePath, attributes | FileAttributes.Archive);

                SetAdminOnlyAcl(lockedFilePath);
                Directory.Delete(folderPath, recursive: true);
            }
            finally
            {
                try { if (File.Exists(tempZip)) { File.Delete(tempZip); } } catch (Exception ex) { Helper.LogError(ex); }
                try { if (Directory.Exists(sessionDir)) { Directory.Delete(sessionDir, recursive: true); } } catch (Exception ex) { Helper.LogError(ex); }
            }
        }

        public static void UnlockFolder(string lockedFilePath, string destinationFolderPath, bool deleteLockedFile = false)
        {
            if (string.IsNullOrWhiteSpace(lockedFilePath))
            {
                throw new ArgumentNullException(nameof(lockedFilePath));
            }

            if (string.IsNullOrWhiteSpace(destinationFolderPath))
            {
                throw new ArgumentNullException(nameof(destinationFolderPath));
            }

            if (!File.Exists(lockedFilePath))
            {
                throw new FileNotFoundException(lockedFilePath);
            }

            if (Directory.Exists(destinationFolderPath))
            {
                throw new IOException("Destination folder already exists: " + destinationFolderPath);
            }

            byte[] protectedKey;
            byte[] nonce = new byte[12];
            byte[] tag = new byte[16];
            byte[] ciphertext;

            using (FileStream fs = new(lockedFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (BinaryReader br = new(fs))
            {
                byte[] magicBytes = br.ReadBytes(Magic.Length);
                if (!IsMagicValid(magicBytes))
                {
                    throw new InvalidDataException("File is not a valid .wflck file.");
                }

                int protectedKeyLength = br.ReadInt32();
                if (protectedKeyLength is <= 0 or > 10_000)
                {
                    throw new InvalidDataException("Invalid protected key length in .wflck file.");
                }

                protectedKey = br.ReadBytes(protectedKeyLength);
                if (protectedKey.Length != protectedKeyLength)
                {
                    throw new EndOfStreamException();
                }

                if (br.Read(nonce, 0, nonce.Length) != nonce.Length)
                {
                    throw new EndOfStreamException();
                }

                if (br.Read(tag, 0, tag.Length) != tag.Length)
                {
                    throw new EndOfStreamException();
                }

                int remaining = (int)(fs.Length - fs.Position);
                ciphertext = br.ReadBytes(remaining);
                if (ciphertext.Length != remaining)
                {
                    throw new EndOfStreamException();
                }
            }

            byte[] key = ProtectedData.Unprotect(protectedKey, null, DataProtectionScope.CurrentUser);
            byte[] plaintext = new byte[ciphertext.Length];

            using (AesGcm aesgcm = new(key, 16))
            {
                aesgcm.Decrypt(nonce, ciphertext, tag, plaintext, null);
            }

            string tempZip = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".zip");
            try
            {
                File.WriteAllBytes(tempZip, plaintext);

                // Ensure destination folder exists before extracting
                _ = Directory.CreateDirectory(destinationFolderPath);
                ZipFile.ExtractToDirectory(tempZip, destinationFolderPath);

                if (deleteLockedFile)
                {
                    try { File.SetAttributes(lockedFilePath, FileAttributes.Normal); } catch { }
                    File.Delete(lockedFilePath);
                }
            }
            finally
            {
                try { if (File.Exists(tempZip)) { File.Delete(tempZip); } } catch (Exception ex) { Helper.LogError(ex); }
                Array.Clear(key, 0, key.Length);
                Array.Clear(plaintext, 0, plaintext.Length);
            }
        }

        public static bool IsLockedFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return false;
            }

            try
            {
                using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                byte[] header = new byte[Magic.Length];
                int read = fs.Read(header, 0, header.Length);
                return read == header.Length && IsMagicValid(header);
            }
            catch (Exception ex)
            {
                Helper.LogError(ex);
                return false;
            }
        }

        private static bool IsMagicValid(byte[] header)
        {
            return header.SequenceEqual(Magic);
        }

        private static void SetAdminOnlyAcl(string path)
        {
            FileInfo fileInfo = new(path);
            try
            {
                // Best-effort: add explicit allow rules for the creating user, Users group (read), Administrators and SYSTEM.
                // Do not change ownership or toggle inheritance to avoid requiring elevated privileges.
                FileSecurity security = fileInfo.GetAccessControl();

                SecurityIdentifier? currentUserSid = WindowsIdentity.GetCurrent()?.User;
                if (currentUserSid != null)
                {
                    security.AddAccessRule(new FileSystemAccessRule(currentUserSid, FileSystemRights.FullControl, AccessControlType.Allow));
                }

                SecurityIdentifier users = new(WellKnownSidType.BuiltinUsersSid, null);
                security.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.ReadAndExecute | FileSystemRights.ReadAttributes | FileSystemRights.ReadData, AccessControlType.Allow));

                SecurityIdentifier system = new(WellKnownSidType.LocalSystemSid, null);
                security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, AccessControlType.Allow));

                SecurityIdentifier admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
                security.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, AccessControlType.Allow));

                fileInfo.SetAccessControl(security);
            }
            catch (Exception ex)
            {
                Helper.LogError(ex);
                // If ACL updates fail (permission issues), continue without failing the lock operation.
            }
        }

        public static string UnlockFolderToTemp(string lockedFilePath)
        {
            if (string.IsNullOrWhiteSpace(lockedFilePath))
            {
                throw new ArgumentNullException(nameof(lockedFilePath));
            }

            if (!File.Exists(lockedFilePath))
            {
                throw new FileNotFoundException(lockedFilePath);
            }

            byte[] protectedKey;
            byte[] nonce = new byte[12];
            byte[] tag = new byte[16];
            byte[] ciphertext;

            using (FileStream fs = new(lockedFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (BinaryReader br = new(fs))
            {
                byte[] magicBytes = br.ReadBytes(Magic.Length);
                if (magicBytes.Length != Magic.Length || !IsMagicValid(magicBytes))
                {
                    throw new InvalidDataException("File is not a valid .wflck file.");
                }

                int protectedKeyLength = br.ReadInt32();
                if (protectedKeyLength is <= 0 or > 10_000)
                {
                    throw new InvalidDataException("Invalid protected key length in .wflck file.");
                }

                protectedKey = br.ReadBytes(protectedKeyLength);
                if (protectedKey.Length != protectedKeyLength)
                {
                    throw new EndOfStreamException();
                }

                int read = br.Read(nonce, 0, nonce.Length);
                if (read != nonce.Length)
                {
                    throw new EndOfStreamException();
                }

                read = br.Read(tag, 0, tag.Length);
                if (read != tag.Length)
                {
                    throw new EndOfStreamException();
                }

                int remaining = (int)(fs.Length - fs.Position);
                ciphertext = br.ReadBytes(remaining);
                if (ciphertext.Length != remaining)
                {
                    throw new EndOfStreamException();
                }
            }

            byte[] key = ProtectedData.Unprotect(protectedKey, null, DataProtectionScope.CurrentUser);
            byte[] plaintext = new byte[ciphertext.Length];

            using (AesGcm aesgcm = new(key, 16))
            {
                aesgcm.Decrypt(nonce, ciphertext, tag, plaintext, null);
            }

            string sessionDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinFolderLock", "Sessions", Path.GetRandomFileName());
            _ = Directory.CreateDirectory(sessionDir);
            string tempZip = Path.Combine(sessionDir, Path.GetRandomFileName() + ".zip");
            string tempExtractPath = Path.Combine(sessionDir, "extracted");

            try
            {
                File.WriteAllBytes(tempZip, plaintext);
                _ = Directory.CreateDirectory(tempExtractPath);
                ZipFile.ExtractToDirectory(tempZip, tempExtractPath);

                return tempExtractPath;
            }
            finally
            {
                try { if (File.Exists(tempZip)) { File.Delete(tempZip); } } catch (Exception ex) { Helper.LogError(ex); }
                Array.Clear(key, 0, key.Length);
                Array.Clear(plaintext, 0, plaintext.Length);
            }
        }
    }
}
