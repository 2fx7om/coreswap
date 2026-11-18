
using System;
using System.IO;
using System.Threading.Tasks;
using CoreSwap.Pages;
using Microsoft.Maui.Controls;

namespace CoreSwap.Services;

public static class FirebasePhotoLoader
{
    public static async Task<ImageSource> LoadAsync(
        string path,
        string? fallbackFile,
        string defaultFile)
    {
        var fallback = ImageSource.FromFile(
            string.IsNullOrWhiteSpace(fallbackFile)
                ? defaultFile
                : fallbackFile);

        try
        {
            var record = await AppServices.Db
                .GetAsync<CoreSwapPhotoRecord>(path);

            if (record is null ||
                string.IsNullOrWhiteSpace(record.Base64))
            {
                return fallback;
            }

            if (!string.Equals(
                    record.ContentType,
                    "image/jpeg",
                    StringComparison.OrdinalIgnoreCase))
            {
                return fallback;
            }

            var bytes = Convert.FromBase64String(
                record.Base64);

            if (bytes.Length == 0 ||
                bytes.Length > 4 * 1024 * 1024)
            {
                return fallback;
            }

            return ImageSource.FromStream(
                () => new MemoryStream(
                    bytes,
                    writable: false));
        }
        catch
        {
            return fallback;
        }
    }
}
 