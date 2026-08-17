using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using JobApplicationBot.Models;
using Microsoft.Extensions.Logging;

namespace JobApplicationBot.Services;

public class JobSearchOutcome
{
    public List<JobSearchResultItem> Results { get; set; } = [];
    /// <summary>Null when posts were fetched successfully (even if zero). Otherwise explains provider failure.</summary>
    public string? HiringPostsStatus { get; set; }
}

public interface IJobScraperService
{
    Task<string> ScrapeJobDescriptionAsync(string url);
    Task<JobSearchOutcome> SearchJobsAsync(string? title, string? experienceLevel, string? datePosted, string? location = null);
}

public class JobScraperService : IJobScraperService
{
    private const int LinkedInPageSize = 10;
    private const int MaxHiringPosts = 20;

    private static readonly HashSet<string> TitleStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "of", "for", "in", "to", "at", "on", "with", "job", "jobs",
        "role", "roles", "position", "positions", "senior", "junior", "mid", "level", "entry"
    };

    private static readonly string[] HiringSignals =
    [
        "hiring", "hire", "we're hiring", "we are hiring", "open role", "open roles",
        "job opening", "vacancy", "vacancies", "looking for", "join our", "join us",
        "recruiter", "opportunity", "وظيفة", "تعيين", "شاغر", "نبحث عن", "مطلوب"
    ];

    /// <summary>Signals that clearly indicate a seniority band (used to drop mismatches).</summary>
    private static readonly Dictionary<string, string[]> ExperienceMismatchSignals = new()
    {
        // Internship — drop senior+ titles
        ["1"] = ["senior", "sr", "sr.", "snr", "lead", "principal", "staff", "director", "head of", "vp", "vice president", "executive", "chief", "cto", "ceo", "cfo"],
        // Entry — drop senior+ and director+
        ["2"] = ["senior", "sr", "sr.", "snr", "lead", "principal", "staff", "director", "head of", "vp", "vice president", "executive", "chief", "cto", "ceo", "cfo"],
        // Associate — drop director+ and intern-only
        ["3"] = ["intern", "internship", "director", "head of", "vp", "vice president", "executive", "chief", "cto", "ceo", "cfo"],
        // Mid-Senior — drop intern/entry-only and exec/director
        ["4"] = ["intern", "internship", "junior", "jr", "jr.", "jnr", "entry level", "entry-level", "graduate", "director", "head of", "vp", "vice president", "executive", "chief", "cto", "ceo", "cfo"],
        // Director — drop intern/junior/entry
        ["5"] = ["intern", "internship", "junior", "jr", "jr.", "jnr", "entry level", "entry-level", "graduate", "associate"],
        // Executive — drop intern/junior/entry/associate
        ["6"] = ["intern", "internship", "junior", "jr", "jr.", "jnr", "entry level", "entry-level", "graduate", "associate"]
    };

    private static readonly Dictionary<string, string[]> LocationAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["egypt"] = ["egypt", "مصر", "cairo", "القاهرة", "giza", "الجيزة", "alexandria", "الإسكندرية", "alex"],
        ["saudi arabia"] = ["saudi", "saudi arabia", "ksa", "السعودية", "riyadh", "الرياض", "jeddah", "جدة"],
        ["united arab emirates"] = ["uae", "emirates", "dubai", "دبي", "abu dhabi", "الإمارات"],
        ["uae"] = ["uae", "emirates", "dubai", "دبي", "abu dhabi", "الإمارات"],
        ["remote"] = ["remote", "work from home", "wfh", "عن بعد"]
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<JobScraperService> _logger;

    public JobScraperService(HttpClient httpClient, ILogger<JobScraperService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }
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

    public async Task<JobSearchOutcome> SearchJobsAsync(
        string? title, string? experienceLevel, string? datePosted, string? location = null)
    {
        // Empty location = worldwide (no country default).
        var resolvedLocation = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
        var jobsTask = FetchLinkedInJobs(title, experienceLevel, datePosted, resolvedLocation);
        var postsTask = FetchLinkedInPosts(title, experienceLevel, resolvedLocation);

        await Task.WhenAll(jobsTask, postsTask);

        var jobs = await jobsTask;
        var (posts, postsStatus) = await postsTask;

        // LinkedIn already applied f_TPR server-side; only drop items provably outside the window.
        jobs = EnforceDateWindow(jobs, datePosted, dropUndated: false);
        // Posts rarely have reliable dates — keep them unless we can prove they are outside the window.
        posts = EnforceDateWindow(posts, datePosted, dropUndated: false);

        jobs = EnforceExperienceLevel(jobs, experienceLevel);
        posts = EnforceExperienceLevel(posts, experienceLevel);

        var merged = new List<JobSearchResultItem>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenTitleCompany = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in jobs.Concat(posts))
        {
            var urlKey = NormalizeUrl(item.Url);
            if (!string.IsNullOrEmpty(urlKey) && !seenUrls.Add(urlKey))
                continue;

            var tcKey = $"{NormalizeKey(item.Title)}|{NormalizeKey(item.Company)}";
            if (!seenTitleCompany.Add(tcKey))
                continue;

            merged.Add(item);
        }

        merged = OrderByRelevance(merged, title);

        return new JobSearchOutcome
        {
            Results = merged,
            HiringPostsStatus = postsStatus
        };
    }

    private async Task<List<JobSearchResultItem>> FetchLinkedInJobs(
        string? title, string? experienceLevel, string? datePosted, string? location)
    {
        var results = new List<JobSearchResultItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var variant in BuildJobSearchVariants(title, experienceLevel, datePosted, location))
        {
            await FetchLinkedInJobsForVariantAsync(
                variant.Title, variant.ExperienceLevel, variant.DatePosted, variant.Location,
                results, seen);
        }

        return results;
    }

    /// <summary>
    /// Broaden only via location aliases when a location is set. Never drop date or experience filters.
    /// </summary>
    private static IEnumerable<(string Title, string? ExperienceLevel, string? DatePosted, string? Location)>
        BuildJobSearchVariants(string? title, string? experienceLevel, string? datePosted, string? location)
    {
        var baseTitle = (title ?? "").Trim();
        if (string.IsNullOrEmpty(baseTitle))
            yield break;

        yield return (baseTitle, experienceLevel, datePosted, location);

        if (string.IsNullOrWhiteSpace(location))
            yield break;

        if (LocationAliases.TryGetValue(location, out var aliases))
        {
            foreach (var alias in aliases.Take(3))
            {
                if (string.Equals(alias, location, StringComparison.OrdinalIgnoreCase))
                    continue;
                yield return (baseTitle, experienceLevel, datePosted, alias);
            }
        }
    }

    private async Task FetchLinkedInJobsForVariantAsync(
        string title,
        string? experienceLevel,
        string? datePosted,
        string? location,
        List<JobSearchResultItem> results,
        HashSet<string> seen)
    {
        var encodedTitle = Uri.EscapeDataString(title);

        for (var start = 0; ; start += LinkedInPageSize)
        {
            var url =
                $"https://www.linkedin.com/jobs-guest/jobs/api/seeMoreJobPostings/search?keywords={encodedTitle}&start={start}";

            if (!string.IsNullOrWhiteSpace(location))
                url += $"&location={Uri.EscapeDataString(location)}";
            else
                url += "&geoId=92000000"; // Worldwide — otherwise LinkedIn defaults to the server's IP region

            if (!string.IsNullOrEmpty(datePosted))
                url += $"&f_TPR={datePosted}";

            if (!string.IsNullOrEmpty(experienceLevel))
                url += $"&f_E={experienceLevel}";

            string html;
            try
            {
                html = await _httpClient.GetStringAsync(url);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LinkedIn jobs page failed at start={Start}", start);
                break;
            }

            var pageItems = ParseLinkedInJobCards(html);
            if (pageItems.Count == 0)
                break;

            var addedThisPage = 0;
            foreach (var item in pageItems)
            {
                var key = NormalizeUrl(item.Url);
                if (!string.IsNullOrEmpty(key) && !seen.Add(key))
                    continue;

                results.Add(item);
                addedThisPage++;
            }

            if (addedThisPage == 0)
                break;
        }
    }

    private static List<JobSearchResultItem> ParseLinkedInJobCards(string html)
    {
        var results = new List<JobSearchResultItem>();
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

    private async Task<(List<JobSearchResultItem> Posts, string? Status)> FetchLinkedInPosts(
        string? title, string? experienceLevel, string? location)
    {
        var results = new List<JobSearchResultItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var safeTitle = (title ?? "").Trim();
        if (string.IsNullOrWhiteSpace(safeTitle))
            return (results, null);

        var locPart = string.IsNullOrWhiteSpace(location) ? "" : $" {location.Trim()}";
        var experiencePhrase = ExperienceLevelToQueryPhrase(experienceLevel);
        var quotedTitle = safeTitle.Contains('"') ? safeTitle : $"\"{safeTitle}\"";
        var experienceBoost = !string.IsNullOrWhiteSpace(experiencePhrase)
            ? $" {experiencePhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]}"
            : "";

        var strictQuery =
            $"site:linkedin.com/posts {quotedTitle}{locPart} (\"we're hiring\" OR \"we are hiring\" OR \"hiring a\" OR \"open role\" OR \"job opening\" OR وظيفة OR تعيين OR شاغر){experienceBoost}";
        var broaderQuery =
            $"site:linkedin.com/posts {quotedTitle}{locPart} (hiring OR job OR recruiter OR وظيفة OR تعيين OR شاغر){experienceBoost}";

        var providerErrors = 0;
        var providerAttempts = 0;
        var searchLocation = location ?? "";

        async Task CollectAsync(Func<string, string, string, Task<(List<JobSearchResultItem> Items, bool Ok)>> fetcher, string query)
        {
            providerAttempts++;
            var (items, ok) = await fetcher(query, safeTitle, searchLocation);
            if (!ok)
                providerErrors++;
            foreach (var item in items)
            {
                var key = NormalizeUrl(item.Url);
                if (!string.IsNullOrEmpty(key) && !seen.Add(key))
                    continue;
                results.Add(item);
            }
        }

        foreach (var query in new[] { strictQuery, broaderQuery }.Distinct(StringComparer.OrdinalIgnoreCase))
            await CollectAsync(FetchDuckDuckGoPostsAsync, query);

        if (results.Count == 0)
        {
            foreach (var query in new[] { broaderQuery, strictQuery }.Distinct(StringComparer.OrdinalIgnoreCase))
                await CollectAsync(FetchBingPostsAsync, query);
        }

        string? status = null;
        if (results.Count == 0 && providerAttempts > 0 && providerErrors == providerAttempts)
            status = "Hiring posts unavailable (search provider blocked or returned no data).";
        else if (results.Count == 0)
            status = "No hiring posts found for this search.";

        var ordered = results
            .OrderByDescending(r => !string.Equals(r.Location, "Location unverified", StringComparison.OrdinalIgnoreCase))
            .ThenBy(r => r.Title)
            .Take(MaxHiringPosts)
            .ToList();

        return (ordered, status);
    }

    private async Task<(List<JobSearchResultItem> Items, bool Ok)> FetchDuckDuckGoPostsAsync(
        string query, string searchTitle, string searchLocation)
    {
        var results = new List<JobSearchResultItem>();
        var encodedQuery = Uri.EscapeDataString(query);
        var url = $"https://html.duckduckgo.com/html/?q={encodedQuery}";

        string html;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Accept", "text/html");
            request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9,ar;q=0.8");
            var response = await _httpClient.SendAsync(request);
            _logger.LogInformation("DDG posts status={Status} len={Len} q={Query}",
                (int)response.StatusCode, response.Content.Headers.ContentLength, query);
            if (!response.IsSuccessStatusCode)
                return (results, false);
            html = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(html) || html.Length < 200)
                return (results, false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DDG hiring-post search failed");
            return (results, false);
        }

        return (ParseWebSearchPostResults(html, searchTitle, searchLocation, isDuckDuckGo: true), true);
    }

    private async Task<(List<JobSearchResultItem> Items, bool Ok)> FetchBingPostsAsync(
        string query, string searchTitle, string searchLocation)
    {
        var results = new List<JobSearchResultItem>();
        var encodedQuery = Uri.EscapeDataString(query);
        var url = $"https://www.bing.com/search?q={encodedQuery}&setlang=en-US";

        string html;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Accept", "text/html");
            request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            var response = await _httpClient.SendAsync(request);
            _logger.LogInformation("Bing posts status={Status} len={Len}",
                (int)response.StatusCode, response.Content.Headers.ContentLength);
            if (!response.IsSuccessStatusCode)
                return (results, false);
            html = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(html) || html.Length < 200)
                return (results, false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bing hiring-post search failed");
            return (results, false);
        }

        return (ParseWebSearchPostResults(html, searchTitle, searchLocation, isDuckDuckGo: false), true);
    }

    private static List<JobSearchResultItem> ParseWebSearchPostResults(
        string html, string searchTitle, string searchLocation, bool isDuckDuckGo)
    {
        var results = new List<JobSearchResultItem>();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var resultNodes = isDuckDuckGo
            ? doc.DocumentNode.SelectNodes("//div[contains(@class,'result')]")
            : doc.DocumentNode.SelectNodes("//li[contains(@class,'b_algo')]");

        if (resultNodes == null)
            return results;

        foreach (var node in resultNodes)
        {
            var linkNode = isDuckDuckGo
                ? node.SelectSingleNode(".//a[contains(@class,'result__a')]")
                : node.SelectSingleNode(".//h2/a");
            var snippetNode = isDuckDuckGo
                ? node.SelectSingleNode(".//*[contains(@class,'result__snippet')]")
                : node.SelectSingleNode(".//div[contains(@class,'b_caption')]//p")
                  ?? node.SelectSingleNode(".//p");

            var postTitle = WebUtility.HtmlDecode(linkNode?.InnerText?.Trim() ?? "");
            var href = linkNode?.GetAttributeValue("href", "") ?? "";
            var postUrl = isDuckDuckGo ? UnwrapDuckDuckGoUrl(href) : href;
            var snippet = WebUtility.HtmlDecode(snippetNode?.InnerText?.Trim() ?? "");

            if (string.IsNullOrEmpty(postTitle)
                || !postUrl.Contains("linkedin.com/posts", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!IsRelevantHiringPost(postTitle, snippet, searchTitle, searchLocation))
                continue;

            var author = ExtractAuthorFromUrl(postUrl);
            var detectedLocation = DetectLocationInText($"{postTitle} {snippet}", searchLocation);

            results.Add(new JobSearchResultItem
            {
                Title = CleanPostTitle(postTitle),
                Company = author,
                Location = detectedLocation ?? "Location unverified",
                Url = postUrl,
                Snippet = snippet.Length > 200 ? snippet[..200] + "..." : snippet,
                ResultType = "Post"
            });
        }

        return results;
    }

    internal static bool IsRelevantHiringPost(
        string postTitle, string snippet, string searchTitle, string searchLocation)
    {
        var haystack = $"{postTitle} {snippet}";
        if (string.IsNullOrWhiteSpace(haystack))
            return false;

        if (!HasHiringSignal(haystack))
            return false;

        if (!HasTitleTokenMatch(haystack, searchTitle))
            return false;

        return true;
    }

    private static bool HasHiringSignal(string text)
    {
        foreach (var signal in HiringSignals)
        {
            if (text.Contains(signal, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool HasTitleTokenMatch(string haystack, string searchTitle)
    {
        var tokens = TokenizeSignificant(searchTitle);
        if (tokens.Count == 0)
            return haystack.Contains(searchTitle.Trim(), StringComparison.OrdinalIgnoreCase);

        var hits = tokens.Count(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase));
        // Relaxed: at least one significant token (majority was too strict for short snippets).
        return hits >= 1;
    }

    private static bool HasLocationMatch(string haystack, string searchLocation)
    {
        var loc = searchLocation.Trim();
        if (haystack.Contains(loc, StringComparison.OrdinalIgnoreCase))
            return true;

        if (LocationAliases.TryGetValue(loc, out var aliases))
            return aliases.Any(a => haystack.Contains(a, StringComparison.OrdinalIgnoreCase));

        var first = loc.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return !string.IsNullOrEmpty(first)
               && first.Length >= 3
               && haystack.Contains(first, StringComparison.OrdinalIgnoreCase);
    }

    private static string? DetectLocationInText(string text, string searchLocation)
    {
        if (!string.IsNullOrWhiteSpace(searchLocation) && HasLocationMatch(text, searchLocation))
            return searchLocation.Trim();

        foreach (var (canonical, aliases) in LocationAliases)
        {
            if (aliases.Any(a => text.Contains(a, StringComparison.OrdinalIgnoreCase)))
                return canonical;
        }

        return null;
    }

    /// <summary>
    /// Drop results whose title/snippet clearly contradict the selected LinkedIn f_E band.
    /// Titles with no seniority signal are kept.
    /// </summary>
    internal static List<JobSearchResultItem> EnforceExperienceLevel(
        List<JobSearchResultItem> items, string? experienceLevel)
    {
        if (string.IsNullOrWhiteSpace(experienceLevel)
            || !ExperienceMismatchSignals.TryGetValue(experienceLevel.Trim(), out var mismatches))
            return items;

        return items.Where(item =>
        {
            var haystack = $"{item.Title} {item.Snippet}";
            return !mismatches.Any(m => ContainsWholePhrase(haystack, m));
        }).ToList();
    }

    private static bool ContainsWholePhrase(string haystack, string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase))
            return false;

        // Word-boundary-ish match so "seniority" does not false-positive on "senior" alone wrongly —
        // we intentionally match "senior" as substring with separators.
        var pattern = $@"(?i)(?<![a-z0-9]){Regex.Escape(phrase)}(?![a-z0-9])";
        return Regex.IsMatch(haystack, pattern);
    }

    /// <summary>
    /// LinkedIn timestamps are usually date-only (midnight). Treat those as end of that day
    /// so a job posted late yesterday is not wrongly counted as older than 24h.
    /// </summary>
    private static DateTime AdjustDateOnly(DateTime utc) =>
        utc.TimeOfDay == TimeSpan.Zero
            ? utc.AddDays(1).AddSeconds(-1)
            : utc;

    /// <summary>
    /// Keep jobs within the LinkedIn f_TPR window. Jobs without a parseable date are dropped when dropUndated is true.
    /// </summary>
    internal static List<JobSearchResultItem> EnforceDateWindow(
        List<JobSearchResultItem> items, string? datePostedFilter, bool dropUndated)
    {
        if (string.IsNullOrWhiteSpace(datePostedFilter))
            return items;

        var maxAge = datePostedFilter switch
        {
            "r86400" => TimeSpan.FromHours(24),
            "r604800" => TimeSpan.FromDays(7),
            "r2592000" => TimeSpan.FromDays(30),
            _ => (TimeSpan?)null
        };

        if (maxAge == null)
            return items;

        var cutoff = DateTime.UtcNow - maxAge.Value;
        var kept = new List<JobSearchResultItem>();

        foreach (var item in items)
        {
            var parsed = TryParsePostedAt(item.DatePosted);
            if (parsed == null)
            {
                if (!dropUndated)
                    kept.Add(item);
                continue;
            }

            if (parsed.Value >= cutoff)
                kept.Add(item);
        }

        return kept;
    }

    internal static DateTime? TryParsePostedAt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var text = raw.Trim();

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return AdjustDateOnly(dt.ToUniversalTime());

        if (DateTime.TryParse(text, out var local))
            return AdjustDateOnly(local.ToUniversalTime());

        // "2 days ago", "3 weeks ago", "Yesterday", "Just now"
        var lower = text.ToLowerInvariant();
        if (lower is "today" or "just now" or "now")
            return DateTime.UtcNow;
        if (lower == "yesterday")
            return DateTime.UtcNow.AddDays(-1);

        var m = Regex.Match(lower, @"(\d+)\s*(minute|minutes|hour|hours|day|days|week|weeks|month|months)\s*ago");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n))
        {
            return m.Groups[2].Value switch
            {
                "minute" or "minutes" => DateTime.UtcNow.AddMinutes(-n),
                "hour" or "hours" => DateTime.UtcNow.AddHours(-n),
                "day" or "days" => DateTime.UtcNow.AddDays(-n),
                "week" or "weeks" => DateTime.UtcNow.AddDays(-7 * n),
                "month" or "months" => DateTime.UtcNow.AddDays(-30 * n),
                _ => null
            };
        }

        return null;
    }

    private static List<JobSearchResultItem> OrderByRelevance(List<JobSearchResultItem> items, string? searchTitle)
    {
        var tokens = TokenizeSignificant(searchTitle ?? "");

        int TitleScore(JobSearchResultItem r)
        {
            if (tokens.Count == 0) return 0;
            return tokens.Count(t => r.Title.Contains(t, StringComparison.OrdinalIgnoreCase));
        }

        DateTime SortDate(JobSearchResultItem r) =>
            TryParsePostedAt(r.DatePosted) ?? DateTime.MinValue;

        return items
            .OrderBy(r => string.Equals(r.ResultType, "Post", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(TitleScore)
            .ThenByDescending(SortDate)
            .ThenBy(r => r.Title)
            .ToList();
    }

    private static List<string> TokenizeSignificant(string title)
    {
        return Regex.Split(title.ToLowerInvariant(), @"[^a-z0-9\.#\+]+")
            .Where(t => t.Length >= 2 && !TitleStopWords.Contains(t))
            .Distinct()
            .ToList();
    }

    private static string ExperienceLevelToQueryPhrase(string? experienceLevel) => experienceLevel switch
    {
        "1" => "intern OR internship",
        "2" => "junior OR entry OR \"entry level\"",
        "3" => "associate OR \"mid level\" OR mid-level",
        "4" => "senior OR \"mid-senior\"",
        "5" => "director OR \"head of\"",
        "6" => "executive OR VP OR \"vice president\" OR C-level",
        _ => ""
    };

    private static string UnwrapDuckDuckGoUrl(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return "";

        try
        {
            var absolute = href.StartsWith("//", StringComparison.Ordinal)
                ? "https:" + href
                : href.StartsWith('/')
                    ? "https://duckduckgo.com" + href
                    : href;

            if (!Uri.TryCreate(absolute, UriKind.Absolute, out var uri))
                return href;

            var query = uri.Query.TrimStart('?');
            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2 && kv[0].Equals("uddg", StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(kv[1]);
            }
        }
        catch
        {
            // fall through
        }

        return href;
    }

    private static string NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "";

        try
        {
            var uri = new Uri(url.Split('?')[0].TrimEnd('/'), UriKind.Absolute);
            return uri.GetLeftPart(UriPartial.Path).TrimEnd('/').ToLowerInvariant();
        }
        catch
        {
            return url.Split('?')[0].TrimEnd('/').ToLowerInvariant();
        }
    }

    private static string NormalizeKey(string? value) =>
        Regex.Replace((value ?? "").Trim().ToLowerInvariant(), @"\s+", " ");

    private static string ExtractAuthorFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2 && segments[0] == "posts")
                return segments[1].Replace("-", " ");
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
