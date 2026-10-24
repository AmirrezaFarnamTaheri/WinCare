using System.Text.Json;
using WinCare.Application.Downloads;

namespace WinCare.Application.Tests;

public sealed class DownloadResumeLabelTests
{
    [Fact]
    public void No_expected_digest_never_claims_byte_range_resume()
    {
        JsonElement jobs = JsonSerializer.SerializeToElement(new[]
        {
            new { id = "without-digest", state = "Interrupted", destination = "archive.zip",
                url = "https://example.com/archive.zip", sha256 = "" }
        });

        DownloadJobRow row = Assert.Single(DownloadJobProjection.Project(jobs));

        Assert.Equal("Restart", row.ResumeButtonLabel);
        Assert.Contains("from the beginning", row.ResumeAccessibleName);
        Assert.Contains("restart", row.StateLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Expected_sha256_offers_conditional_continuation_without_claiming_guaranteed_resume()
    {
        JsonElement jobs = JsonSerializer.SerializeToElement(new[]
        {
            new { id = "with-digest", state = "Interrupted", destination = "archive.zip",
                url = "https://example.com/archive.zip", sha256 = new string('a', 64) }
        });

        DownloadJobRow row = Assert.Single(DownloadJobProjection.Project(jobs));

        Assert.Equal("Continue", row.ResumeButtonLabel);
        Assert.Contains("may be reused", row.ResumeAccessibleName);
        Assert.Contains("continue", row.StateLabel, StringComparison.OrdinalIgnoreCase);
    }
}
