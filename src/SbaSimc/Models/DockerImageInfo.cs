namespace SbaSimc.Models;

public record DockerImageInfo(string Tag, string? Digest, string? ImageId)
{
    /// <summary>Human-readable label for the resolved image (tag + digest or image ID).</summary>
    public string DisplayLabel =>
        Digest is not null ? $"{Tag} @ {Digest}"
        : ImageId is not null ? $"{Tag} ({ImageId})"
        : Tag;

    /// <summary>Compact digest for display; full value remains in <see cref="Digest"/>.</summary>
    public string? DigestShort
    {
        get
        {
            if (Digest is null)
                return null;

            const string marker = "@sha256:";
            var markerIndex = Digest.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex >= 0)
            {
                var hash = Digest[(markerIndex + marker.Length)..];
                return hash.Length <= 12 ? $"sha256:{hash}" : $"sha256:{hash[..12]}…";
            }

            return Digest.Length <= 24 ? Digest : $"{Digest[..24]}…";
        }
    }
}
