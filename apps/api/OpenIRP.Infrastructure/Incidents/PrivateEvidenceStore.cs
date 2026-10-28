using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Extensions.Configuration;
using OpenIRP.Application.Identity;
using OpenIRP.Application.Incidents;

namespace OpenIRP.Infrastructure.Incidents;

public sealed class PrivateEvidenceStore(IConfiguration configuration) : IPrivateEvidenceStore
{
    public const int MaxBytes = 10 * 1024 * 1024;
    private string Root()
    {
        var configured = configuration["EvidenceFiles:RootPath"];
        if (string.IsNullOrWhiteSpace(configured)) throw new IdentityOperationException(503, "file_storage_not_configured");
        if (!Path.IsPathFullyQualified(configured) || configured.StartsWith(@"\\"))
            throw new IdentityOperationException(503, "invalid_private_storage_root");
        var root = Path.GetFullPath(configured).TrimEnd(Path.DirectorySeparatorChar);
        if (root.Length < 4 || root.Contains(Path.DirectorySeparatorChar + "wwwroot" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || root.EndsWith(Path.DirectorySeparatorChar + "wwwroot", StringComparison.OrdinalIgnoreCase)
            || root.Contains(Path.DirectorySeparatorChar + "public" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || root.EndsWith(Path.DirectorySeparatorChar + "public", StringComparison.OrdinalIgnoreCase))
            throw new IdentityOperationException(503, "invalid_private_storage_root");
        for (var ancestor = new DirectoryInfo(root); ancestor is not null; ancestor = ancestor.Parent)
            if (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0) throw new EvidenceStorageException("unsafe_storage_path");
        var directory = Directory.CreateDirectory(root);
        if (OperatingSystem.IsWindows())
        {
            var owner = WindowsIdentity.GetCurrent().User!;
            var acl = new DirectorySecurity();
            acl.SetOwner(owner); acl.SetAccessRuleProtection(true, false);
            acl.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(acl);
        }
        else File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        foreach (var child in new[] { "staging", "objects" })
        {
            var sub = Directory.CreateDirectory(Path.Combine(root, child));
            if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) throw new EvidenceStorageException("unsafe_storage_path");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(sub.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return root;
    }
    private string Location(Guid id, bool staged)
    {
        if (id == Guid.Empty) throw new EvidenceStorageException("unsafe_storage_path");
        var path = Path.Combine(Root(), staged ? "staging" : "objects", id.ToString("N") + (staged ? ".pending" : ".bin"));
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new EvidenceStorageException("unsafe_storage_path");
        return path;
    }
    public async Task<StagedEvidenceFile> StageAsync(Stream source, string name, string contentType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.Any(char.IsControl) || name.IndexOfAny("\\/:*?\"<>|".ToCharArray()) >= 0)
            throw new IdentityOperationException(400, "invalid_evidence_filename");
        var expected = Path.GetExtension(name).ToLowerInvariant() switch
        { ".pdf" => "application/pdf", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".txt" => "text/plain", _ => "" };
        var mime = contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (expected.Length == 0 || mime != expected) throw new IdentityOperationException(415, "unsupported_evidence_file_type");
        var id = Guid.NewGuid(); var path = Location(id, true);
        try
        {
            long size = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                var bytes = new byte[65536]; int count;
                while ((count = await source.ReadAsync(bytes, ct)) != 0)
                {
                    size += count;
                    if (size > MaxBytes) throw new IdentityOperationException(413, "evidence_file_too_large");
                    hash.AppendData(bytes, 0, count); await output.WriteAsync(bytes.AsMemory(0, count), ct);
                }
                await output.FlushAsync(ct);
            }
            if (size == 0) throw new IdentityOperationException(400, "empty_evidence_file");
            var body = await File.ReadAllBytesAsync(path, ct);
            var valid = mime switch
            {
                "image/png" => body.Length >= 8 && body.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}),
                "image/jpeg" => body.Length >= 5 && body[0]==255 && body[1]==216 && body[2]==255 && body[^2]==255 && body[^1]==217,
                "application/pdf" => body.Length >= 10 && body.AsSpan(0,5).SequenceEqual("%PDF-"u8) && Encoding.ASCII.GetString(body.AsSpan(Math.Max(0,body.Length-1024))).Contains("%%EOF",StringComparison.Ordinal),
                _ => ValidText(body)
            };
            if (!valid) throw new IdentityOperationException(415, "evidence_file_signature_mismatch");
            return new(id, name, mime, size, Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }
    private static bool ValidText(byte[] bytes)
    {
        try { return !new UTF8Encoding(false,true).GetString(bytes).Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')); }
        catch (DecoderFallbackException) { return false; }
    }
    public async Task PublishAsync(StagedEvidenceFile file, CancellationToken ct)
    {
        var target = Location(file.StorageId, false);
        if (!File.Exists(target))
        {
            var stage = Location(file.StorageId, true);
            if (!File.Exists(stage))
            {
                if (!File.Exists(target)) throw new EvidenceStorageException("missing_file");
            }
            else
            {
                try { File.Move(stage, target, false); }
                catch (IOException) when (File.Exists(target)) { }
            }
        }
        await using var verified = await OpenVerifiedAsync(file, ct);
    }
    public async Task<Stream> OpenVerifiedAsync(StagedEvidenceFile file, CancellationToken ct)
    {
        var path = Location(file.StorageId, false);
        if (!File.Exists(path)) throw new EvidenceStorageException("missing_file");
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            if (stream.Length != file.Length || !CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(stream,ct),Convert.FromHexString(file.Sha256)))
                throw new EvidenceStorageException("integrity_check_failed");
            stream.Position = 0; return stream;
        }
        catch { await stream.DisposeAsync(); throw; }
    }
    public Task DiscardUnregisteredAsync(Guid storageId)
    {
        var path = Location(storageId,true);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}
