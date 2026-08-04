using JobApplicationBot.Data.Entities;

namespace JobApplicationBot.Data.Repositories;

public record CvFileMetadata(string FileName, string ContentType, long SizeBytes, DateTime UploadedAt);

public interface IUserCvFileRepository
{
    /// <summary>
    /// Returns the full CV row including the byte content. Use only when the bytes are needed
    /// (download or email send).
    /// </summary>
    Task<UserCvFile?> GetAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Returns just the metadata (no blob), so SQL Server does not stream the content column.
    /// Used by views that just need to show "CV.pdf - 4.2 MB".
    /// </summary>
    Task<CvFileMetadata?> GetMetadataAsync(string userId, CancellationToken ct = default);

    Task UpsertAsync(UserCvFile cv, CancellationToken ct = default);

    Task DeleteAsync(string userId, CancellationToken ct = default);
}
