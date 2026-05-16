namespace GitHubAdaptor.Models;

public class PullRequestPayload
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("pull_request")]
    public PullRequest? PullRequest { get; set; }

    [JsonPropertyName("repository")]
    public Repository? Repository { get; set; }

    [JsonPropertyName("installation")]
    public Installation? Installation { get; set; }
}

public class PullRequest
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    [JsonPropertyName("base")]
    public PullRequestBranch? Base { get; set; }

    [JsonPropertyName("head")]
    public PullRequestBranch? Head { get; set; }
}

public class PullRequestBranch
{
    [JsonPropertyName("ref")]
    public string Ref { get; set; } = "";

    [JsonPropertyName("sha")]
    public string Sha { get; set; } = "";
}

public class Repository
{
    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("owner")]
    public RepositoryOwner? Owner { get; set; }
}

public class RepositoryOwner
{
    [JsonPropertyName("login")]
    public string Login { get; set; } = "";
}

public class Installation
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
}