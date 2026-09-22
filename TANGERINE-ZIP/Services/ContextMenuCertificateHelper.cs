using Microsoft.Win32;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;

namespace TANGERINE_ZIP.Services;

// Stage head: CTXCH
internal static class ContextMenuCertificateHelper
{
    internal const string InstallSwitch = "--context-cert-install";
    internal const string RemoveSwitch = "--context-cert-remove";
    internal const int CertificatePreexisting = 10;
    internal const int CertificateShared = 11;
    internal const string OwnershipPath = @"SOFTWARE\TangerineZip\ContextMenuCertificates";

    internal static int Execute(string[] args)
    {
        try
        {
            if (args.Length != 2) return 2;
            string ownerSid = new SecurityIdentifier(args[1]).Value;
            using X509Certificate2 certificate = ContextMenuRegistrationService.LoadEmbeddedCertificate();
            return args[0] switch
            {
                InstallSwitch => Install(certificate, ownerSid),
                RemoveSwitch => Remove(certificate, ownerSid),
                _ => 2
            };
        }
        catch
        {
            // The nonzero exit code is converted into a localized StageException by
            // the unelevated process, which owns all user interaction. //CTXCH0001
            return 4;
        }
    }

    private static int Install(X509Certificate2 certificate, string ownerSid)
    {
        using X509Store store = new(StoreName.TrustedPeople, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        bool exists = store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false).Count > 0;
        using RegistryKey ownershipRoot = Registry.LocalMachine.CreateSubKey(OwnershipPath, writable: true)
            ?? throw new InvalidOperationException(); //CTXCH0002
        using RegistryKey? existingOwnership = ownershipRoot.OpenSubKey(certificate.Thumbprint, writable: true);
        if (exists)
        {
            object? marker = existingOwnership?.GetValue("CreatedByTzip");
            if (marker is not int createdByTzip || createdByTzip != 1) return CertificatePreexisting;
        }

        if (!exists) store.Add(certificate);
        using RegistryKey ownership = existingOwnership ?? ownershipRoot.CreateSubKey(certificate.Thumbprint, writable: true)
            ?? throw new InvalidOperationException(); //CTXCH0003
        ownership.SetValue("CreatedByTzip", 1, RegistryValueKind.DWord);
        string[] owners = ReadOwners(ownership).Append(ownerSid).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        ownership.SetValue("OwnerSids", owners, RegistryValueKind.MultiString);
        return 0;
    }

    private static int Remove(X509Certificate2 certificate, string ownerSid)
    {
        using RegistryKey? ownershipRoot = Registry.LocalMachine.OpenSubKey(OwnershipPath, writable: true);
        using RegistryKey? ownership = ownershipRoot?.OpenSubKey(certificate.Thumbprint, writable: true);
        if (ownership?.GetValue("CreatedByTzip") is not int createdByTzip || createdByTzip != 1)
            return CertificatePreexisting;

        string[] remainingOwners = ReadOwners(ownership)
            .Where(value => !value.Equals(ownerSid, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (remainingOwners.Length > 0)
        {
            ownership.SetValue("OwnerSids", remainingOwners, RegistryValueKind.MultiString);
            return CertificateShared;
        }

        using X509Store store = new(StoreName.TrustedPeople, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        foreach (X509Certificate2 match in store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false))
        {
            using (match) store.Remove(match);
        }
        ownership.Close();
        ownershipRoot?.DeleteSubKeyTree(certificate.Thumbprint, throwOnMissingSubKey: false);
        return 0;
    }

    private static string[] ReadOwners(RegistryKey ownership) =>
        ownership.GetValue("OwnerSids") as string[] ?? [];
}
