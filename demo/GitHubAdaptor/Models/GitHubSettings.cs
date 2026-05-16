namespace GitHubAdaptor.Models;

public class GitHubSettings
{
    public long AppId { get; set; }
    public string PrivateKeyPath { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string WebhookEndpoint { get; set; } = "/github/webhook";
}