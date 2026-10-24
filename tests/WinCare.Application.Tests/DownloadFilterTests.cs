using System.Text.Json;
using WinCare.Application.Downloads;

namespace WinCare.Application.Tests;

public sealed class DownloadFilterTests
{
    [Fact]
    public void Search_and_state_filters_intersect_without_exposing_urls()
    {
        using var payload = JsonDocument.Parse("""
        [
          {"id":"1","state":"Pending","destination":"alpha.zip","url":"https://cdn.example.com/alpha.zip?token=secret1"},
          {"id":"2","state":"Completed","destination":"report.pdf","url":"https://docs.example.org/report.pdf?token=secret2"},
          {"id":"3","state":"Failed","destination":"beta.zip","url":"https://downloads.example.org/beta.zip?token=secret3"}
        ]
        """);
        IReadOnlyList<DownloadJobRow> jobs = DownloadJobProjection.Project(payload.RootElement);
        Assert.Equal(2, DownloadViewFilters.Apply(jobs, "All", "ZIP").Count);
        Assert.Equal("beta.zip", Assert.Single(DownloadViewFilters.Apply(jobs, "Finished", "zip")).Name);
        Assert.Equal("report.pdf", Assert.Single(DownloadViewFilters.Apply(jobs, "Finished", "docs.example.org")).Name);
        Assert.Equal(2, DownloadViewFilters.Apply(jobs, "Finished", "").Count);
        Assert.Empty(DownloadViewFilters.Apply(jobs, "All", "secret2"));
        Assert.Equal(3, jobs.Count);
    }

    [Fact]
    public void Interrupted_download_does_not_promise_validated_byte_resume()
    {
        using var payload = JsonDocument.Parse("""
            [{"id":"restart-me","state":"Interrupted","destination":"archive.zip","url":"https://example.com/archive.zip","partialBytes":4096}]
            """);

        DownloadJobRow job = Assert.Single(DownloadJobProjection.Project(payload.RootElement));

        Assert.True(job.CanResume);
        Assert.Contains("restart", job.StateLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("restart", job.ResumeAccessibleName, StringComparison.OrdinalIgnoreCase);
    }
}
