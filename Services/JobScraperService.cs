using System.Net;
using HtmlAgilityPack;
using JobApplicationBot.Models;

namespace JobApplicationBot.Services;

public interface IJobScraperService
{
    Task<string> ScrapeJobDescriptionAsync(string url);
    Task<List<JobSearchResultItem>> SearchJobsAsync(string? title, string? experienceLevel, string? datePosted);
}

public class JobScraperService : IJobScraperService
{
    private const string DefaultLocation = "Egypt";
    private readonly HttpClient _httpClient;

    public JobScraperService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    public async Task<string> ScrapeJobDescriptionAsync(string url)
    {
        var response = await _httpClient.GetStringAsync(url);
        var doc = new HtmlDocument();
        doc.LoadHtml(response);

        var nodesToRemove = doc.DocumentNode.SelectNodes("//script|//style|//nav|//footer|//header");
        if (nodesToRemove != null)
        {
            foreach (var node in nodesToRemove)
                node.Remove();
        }

        var textContent = doc.DocumentNode.InnerText;

        var lines = textContent.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l));

        return string.Join("\n", lines);
    }

    public async Task<List<JobSearchResultItem>> SearchJobsAsync(
        string? title, string? experienceLevel, string? datePosted)
    {
        var jobsTask = FetchLinkedInJobs(title, experienceLevel, datePosted);
        var postsTask = FetchLinkedInPosts(title);

        await Task.WhenAll(jobsTask, postsTask);

        var jobs = await jobsTask;
        var posts = await postsTask;

        var merged = new List<JobSearchResultItem>();
        merged.AddRange(jobs);
        merged.AddRange(posts);

        return merged;
    }

    private async Task<List<JobSearchResultItem>> FetchLinkedInJobs(
        string? title, string? experienceLevel, string? datePosted)
    {
        var results = new List<JobSearchResultItem>();
        var encodedTitle = Uri.EscapeDataString(title?.Trim() ?? "");
        var encodedLocation = Uri.EscapeDataString(DefaultLocation);

        var url = $"https://www.linkedin.com/jobs-guest/jobs/api/seeMoreJobPostings/search?keywords={encodedTitle}&location={encodedLocation}&start=0";

        if (!string.IsNullOrEmpty(datePosted))
            url += $"&f_TPR={datePosted}";

        if (!string.IsNullOrEmpty(experienceLevel))
            url += $"&f_E={experienceLevel}";

        string html;
        try
        {
            html = await _httpClient.GetStringAsync(url);
        }
        catch
        {
            return results;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var jobCards = doc.DocumentNode.SelectNodes("//li");
        if (jobCards == null)
            return results;

        foreach (var card in jobCards)
        {
            var titleNode = card.SelectSingleNode(".//h3[contains(@class,'base-search-card__title')]");
            var companyNode = card.SelectSingleNode(".//h4[contains(@class,'base-search-card__subtitle')]");
            var locationNode = card.SelectSingleNode(".//span[contains(@class,'job-search-card__location')]");
            var linkNode = card.SelectSingleNode(".//a[contains(@class,'base-card__full-link')]");
            var dateNode = card.SelectSingleNode(".//time[contains(@class,'job-search-card__listdate')]")
                        ?? card.SelectSingleNode(".//time");
            var imgNode = card.SelectSingleNode(".//img[contains(@class,'artdeco-entity-image')]")
                       ?? card.SelectSingleNode(".//img");

            var jobTitle = WebUtility.HtmlDecode(titleNode?.InnerText?.Trim() ?? "");
            if (string.IsNullOrEmpty(jobTitle))
                continue;

            results.Add(new JobSearchResultItem
            {
                Title = jobTitle,
                Company = WebUtility.HtmlDecode(companyNode?.InnerText?.Trim() ?? "Unknown"),
                Location = WebUtility.HtmlDecode(locationNode?.InnerText?.Trim() ?? ""),
                Url = linkNode?.GetAttributeValue("href", "") ?? "",
                DatePosted = dateNode?.GetAttributeValue("datetime", dateNode.InnerText?.Trim() ?? "") ?? "",
                LogoUrl = imgNode?.GetAttributeValue("data-delayed-url", "")
                       ?? imgNode?.GetAttributeValue("src", ""),
                ResultType = "Job"
            });
        }

        return results;
    }

    private async Task<List<JobSearchResultItem>> FetchLinkedInPosts(string? title)
    {
        var results = new List<JobSearchResultItem>();
        var query = $"site:linkedin.com/posts \"{title}\" Egypt hiring OR job OR وظيفة";
        var encodedQuery = Uri.EscapeDataString(query);
        var url = $"https://html.duckduckgo.com/html/?q={encodedQuery}";

        string html;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "text/html");
            request.Headers.Add("Accept-Language", "en-US,en;q=0.9");
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            html = await response.Content.ReadAsStringAsync();
        }
        catch
        {
            return results;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var resultNodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'result')]");
        if (resultNodes == null)
            return results;

        foreach (var node in resultNodes)
        {
            var linkNode = node.SelectSingleNode(".//a[contains(@class,'result__a')]");
            var snippetNode = node.SelectSingleNode(".//*[contains(@class,'result__snippet')]");

            var postTitle = WebUtility.HtmlDecode(linkNode?.InnerText?.Trim() ?? "");
            var postUrl = linkNode?.GetAttributeValue("href", "") ?? "";
            var snippet = WebUtility.HtmlDecode(snippetNode?.InnerText?.Trim() ?? "");

            if (string.IsNullOrEmpty(postTitle) || !postUrl.Contains("linkedin.com/posts"))
                continue;

            var author = ExtractAuthorFromUrl(postUrl);

            results.Add(new JobSearchResultItem
            {
                Title = CleanPostTitle(postTitle),
                Company = author,
                Location = "Egypt",
                Url = postUrl,
                Snippet = snippet.Length > 200 ? snippet[..200] + "..." : snippet,
                ResultType = "Post"
            });
        }

        return results;
    }

    private static string ExtractAuthorFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2 && segments[0] == "posts")
            {
                return segments[1].Replace("-", " ");
            }
        }
        catch { }
        return "LinkedIn User";
    }

    private static string CleanPostTitle(string title)
    {
        var cleaned = title
            .Replace(" | LinkedIn", "")
            .Replace(" - LinkedIn", "")
            .Trim();

        if (cleaned.Length > 100)
            cleaned = cleaned[..100] + "...";

        return cleaned;
    }
}
