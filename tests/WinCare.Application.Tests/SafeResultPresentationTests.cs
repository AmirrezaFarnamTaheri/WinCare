using System.Text.Json;
using WinCare.App.ViewModels.Pages;
using Xunit;

namespace WinCare.Application.Tests;

public sealed class SafeResultPresentationTests
{
    [Fact]
    public void Signed_urls_and_nested_credentials_are_hidden_in_result_details()
    {
        JsonElement data = JsonSerializer.SerializeToElement(new
        {
            url = "https://alice:password@files.example.com/report.zip?token=secret-value",
            destination = "report.zip",
            details = new
            {
                headers = new { authorization = "Bearer bearer-secret" },
                note = "Downloaded from https://files.example.com/private?signature=signed-secret"
            }
        });

        string view = JsonSerializer.Serialize(SafeResultPresentation.SanitizeData(data));
        Assert.Contains("report.zip", view);
        Assert.Contains("[redacted]", view);
        Assert.DoesNotContain("secret-value", view);
        Assert.DoesNotContain("bearer-secret", view);
        Assert.DoesNotContain("signed-secret", view);
        Assert.DoesNotContain("alice:password", view);
    }

    [Fact]
    public void Ordinary_messages_are_kept_but_link_paths_are_not_shown()
    {
        Assert.Equal("Completed.", SafeResultPresentation.SanitizeText("Completed."));
        Assert.Equal("https://example.com/[link hidden]",
            SafeResultPresentation.SanitizeText("https://example.com/private?key=abcdef"));
    }
}
