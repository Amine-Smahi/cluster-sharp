namespace ClusterSharp.Api.Helpers;

public static class GithubHelper
{
    private const string AmineSmahi = "Amine-Smahi";
        
    public static string GetRepoName(string url) =>
        string.IsNullOrEmpty(url)
            ? throw new ArgumentException("URL must not be null or empty.", nameof(url))
            : url.Contains(AmineSmahi)
                ? url.Split([$"{AmineSmahi}/"], StringSplitOptions.None)[1]
                    .Split(['/', '?'], StringSplitOptions.RemoveEmptyEntries)[0]
                : throw new ArgumentException($"URL must contain '{AmineSmahi}/'", nameof(url));

    public static bool IsValidSource(string url) => url.StartsWith($"github.com/{AmineSmahi}");
}