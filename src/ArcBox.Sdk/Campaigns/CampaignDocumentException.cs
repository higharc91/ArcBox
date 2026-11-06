using System.Text.Json;

namespace ArcBox.Sdk.Campaigns;

/// <summary>A campaign read refusal whose safe explanation can be shown to the whole room.</summary>
public sealed class CampaignDocumentException(string message, Exception? inner = null) : JsonException(message, inner)
{
    public const string NeedsAttention = "This campaign file needs attention. The saved campaign has not been changed. Ask the host to check the campaign file before trying again.";
    public const string NewerVersion = "This campaign was saved by a newer version. The saved campaign has not been changed. Update the server before trying again.";
    public const string MissingContent = "This campaign uses a class or item missing from the current content pack. The saved campaign has not been changed. Restore the matching content pack before trying again.";
}
